using System;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Management.IotHub;
using Microsoft.Azure.Management.IotHub.Models;
using Microsoft.Rest;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using Microsoft.Rest.Azure;
using IotHubScale.Core;

namespace IotHubScale
{
    public static class IotHubScale
    {
        // specifically need a named instance Id to implement stateful singleton pattern
        const string IotHubScaleOrchestratorInstanceId = "IotHubScaleOrchestrator_1";
        const string IotHubScaleOrchestratorName = nameof(IotHubScaleOrchestrator);
        const string IotHubScaleWorkerName = nameof(IotHubScaleWorker);

        // function configuration and authentication data
        // hard coded for the sample.  For production, look at something like KeyVault for storing secrets
        // more info here-> https://blogs.msdn.microsoft.com/dotnet/2016/10/03/storing-and-using-secrets-in-azure/
        const double JobFrequencyMinutes = 5;
        static string ApplicationId = "<application id>";
        static string SubscriptionId = "<subscription id>";
        static string TenantId = "<tenant id>";
        static string ApplicationPassword = "<application password>";
        static string ResourceGroupName = "<resource group containing iothub>";
        static string IotHubName = "<short iothub name>";
        static int ThresholdPercentage = 90;

        // "launcher" function.  runs periodically on timer trigger and just makes sure one (and only one)
        // instance of the orchestrator is running
        [FunctionName("IotHubScaleInit")]
        public static async Task IotHubScaleInit(
                [TimerTrigger("0 0 * * * *")]TimerInfo myTimer,
                [OrchestrationClient] DurableOrchestrationClient starter,
                TraceWriter log)
        {
            log.Info($"C# Timer trigger function executed at: {DateTime.Now}");

            // check and see if a named instance of the orchestrator is already running
            var existingInstance = await starter.GetStatusAsync(IotHubScaleOrchestratorInstanceId);
            if (existingInstance == null)
            {
                log.Info(String.Format("{0} job not running, starting new instance...", IotHubScaleOrchestratorInstanceId));
                await starter.StartNewAsync(IotHubScaleOrchestratorName, IotHubScaleOrchestratorInstanceId, input: null);
            }
            else
                log.Info(String.Format("An instance of {0} job is already running, nothing to do...", IotHubScaleOrchestratorInstanceId));
        }

        // the orchestrator function...  manages the call to the actual worker, then sets a timer to
        // have the Durable Functions framework restart it in X minutes
        [FunctionName(IotHubScaleOrchestratorName)]
        public static async Task IotHubScaleOrchestrator(
                [OrchestrationTrigger] DurableOrchestrationContext context,
                TraceWriter log)
        {
            log.Info("IotHubScaleOrchestrator started");

            // launch and wait on the "worker" function
            await context.CallActivityAsync(IotHubScaleWorkerName);

            // register a timer with the durable functions infrastructure to re-launch the orchestrator in the future
            DateTime wakeupTime = context.CurrentUtcDateTime.Add(TimeSpan.FromMinutes(JobFrequencyMinutes));
            await context.CreateTimer(wakeupTime, CancellationToken.None);

            log.Info(String.Format("IotHubScaleOrchestrator done...  tee'ing up next instance in {0} minutes.", JobFrequencyMinutes.ToString()));

            // end this 'instance' of the orchestrator and schedule another one to start based on the timer above
            context.ContinueAsNew(null);
        }

        // worker function - does the actual work of scaling the IoTHub
        [FunctionName(IotHubScaleWorkerName)]
        public static void IotHubScaleWorker(
            [ActivityTrigger] DurableActivityContext context,
            TraceWriter log)
        {
            // connect management lib to iotHub
            IotHubClient client = GetNewIotHubClient(log);
            if (client == null)
            {
                log.Error("Unable to create IotHub client");
                return;
            } 

            // get IotHub properties, the most important of which for our use is the current Sku details
            IotHubDescription desc = client.IotHubResource.Get(ResourceGroupName, IotHubName);
            string currentSKU = desc.Sku.Name;
            long currentUnits = desc.Sku.Capacity;

            // get current "used" message count for the IotHub
            long currentMessageCount = -1;
            IPage<IotHubQuotaMetricInfo> mi = client.IotHubResource.GetQuotaMetrics(ResourceGroupName, IotHubName);
            foreach (IotHubQuotaMetricInfo info in mi)
            {
                if (info.Name == "TotalMessages")
                    currentMessageCount = (long) info.CurrentValue;
            }
            if(currentMessageCount < 0)
            {
                log.Error("Unable to retreive current message count for IoTHub");
                return;
            }

            ScaleDecision decision = ScaleLogic.EvaluateScale(currentSKU, currentUnits, currentMessageCount, ThresholdPercentage);

            log.Info("Current SKU Tier: " + desc.Sku.Tier);
            log.Info("Current SKU Name: " + decision.CurrentSku);
            log.Info("Current SKU Capacity: " + decision.CurrentUnits.ToString());
            log.Info("Current Message Count:  " + decision.CurrentMessageCount.ToString());
            log.Info("Current Sku/Unit Message Threshold:  " + decision.MessageLimit);

            if (!decision.ShouldScale)
            {
                log.Info(decision.Reason);
                return;
            }

            log.Info(decision.Reason);

            // update the IoT Hub description with the new sku level and units
            desc.Sku.Name = decision.TargetSku;
            desc.Sku.Capacity = decision.TargetUnits;

            // scale the IoT Hub by submitting the new configuration (tier and units)
            DateTime dtStart = DateTime.Now;
            client.IotHubResource.CreateOrUpdate(ResourceGroupName, IotHubName, desc);
            TimeSpan ts = new TimeSpan(DateTime.Now.Ticks - dtStart.Ticks);

            log.Info(String.Format("Updated IoTHub {0} from {1}-{2} to {3}-{4} in {5} seconds", IotHubName, currentSKU, currentUnits, decision.TargetSku, decision.TargetUnits, ts.Seconds));

            //  this would be a good place to send notifications that you scaled up the hub :-)
        }

        // authenticate to Azure AD and get a token to acccess the the IoT Hub on behalf of our "application"
        private static IotHubClient GetNewIotHubClient(TraceWriter log)
        {
            var authContext = new AuthenticationContext(string.Format("https://login.microsoftonline.com/{0}", TenantId));
            var credential = new ClientCredential(ApplicationId, ApplicationPassword);
            AuthenticationResult token = authContext.AcquireTokenAsync("https://management.core.windows.net/", credential).Result;
            if (token == null)
            {
                log.Error("Failed to obtain the authentication token");
                return null;
            }

            var creds = new TokenCredentials(token.AccessToken);
            var client = new IotHubClient(creds);
            client.SubscriptionId = SubscriptionId;

            return client;
        }

        // get the new sku/units target for scaling the IoT Hub
    }
}
