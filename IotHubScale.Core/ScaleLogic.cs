using System;

namespace IotHubScale.Core
{
    public class ScaleDecision
    {
        public bool ShouldScale { get; set; }
        public string CurrentSku { get; set; }
        public long CurrentUnits { get; set; }
        public long CurrentMessageCount { get; set; }
        public long MessageLimit { get; set; }
        public string TargetSku { get; set; }
        public long TargetUnits { get; set; }
        public string Reason { get; set; }
    }

    public static class ScaleLogic
    {
        public static ScaleDecision EvaluateScale(string currentSku, long currentUnits, long currentMessageCount, int thresholdPercent)
        {
            long messageLimit = GetSkuUnitThreshold(currentSku, currentUnits, thresholdPercent);

            if (currentMessageCount < messageLimit)
            {
                return new ScaleDecision
                {
                    ShouldScale = false,
                    CurrentSku = currentSku,
                    CurrentUnits = currentUnits,
                    CurrentMessageCount = currentMessageCount,
                    MessageLimit = messageLimit,
                    TargetSku = currentSku,
                    TargetUnits = currentUnits,
                    Reason = string.Format("Current message count of {0} is less than the threshold of {1}. Nothing to do", currentMessageCount, messageLimit)
                };
            }

            long newSkuUnits = GetScaleUpTarget(currentSku, currentUnits);
            if (newSkuUnits < 0)
            {
                return new ScaleDecision
                {
                    ShouldScale = false,
                    CurrentSku = currentSku,
                    CurrentUnits = currentUnits,
                    CurrentMessageCount = currentMessageCount,
                    MessageLimit = messageLimit,
                    TargetSku = currentSku,
                    TargetUnits = currentUnits,
                    Reason = "Unable to determine new scale units for IoTHub (perhaps you are already at the highest units for a tier?)"
                };
            }

            return new ScaleDecision
            {
                ShouldScale = true,
                CurrentSku = currentSku,
                CurrentUnits = currentUnits,
                CurrentMessageCount = currentMessageCount,
                MessageLimit = messageLimit,
                TargetSku = currentSku,
                TargetUnits = newSkuUnits,
                Reason = string.Format("Current message count of {0} is over the threshold of {1}. Need to scale IotHub", currentMessageCount, messageLimit)
            };
        }

        public static long GetScaleUpTarget(string currentSku, long currentUnits)
        {
            switch (currentSku)
            {
                case "S1":
                    if (currentUnits <= 199)
                        return currentUnits + 1;
                    break;
                case "S2":
                    if (currentUnits <= 199)
                        return currentUnits + 1;
                    break;
                case "S3":
                    if (currentUnits <= 9)
                        return currentUnits + 1;
                    break;
            }

            return -1;
        }

        public static long GetSkuUnitThreshold(string sku, long units, int percent)
        {
            long multiplier = 0;
            switch (sku)
            {
                case "S1":
                    multiplier = 400000;
                    break;
                case "S2":
                    multiplier = 6000000;
                    break;
                case "S3":
                    multiplier = 300000000;
                    break;
            }

            return (multiplier * units * percent) / 100;
        }
    }
}
