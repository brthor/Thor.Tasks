using System;
using System.Threading.Tasks;
using Gofer.NET.Utils;
using NCrontab;
using Newtonsoft.Json;

namespace Gofer.NET
{
    [JsonObject(MemberSerialization.OptIn)]
    public class RecurringTask
    {
        public string LockKey => $"{nameof(RecurringTask)}::{TaskKey}::ScheduleLock";

        [JsonProperty]
        public TaskKey TaskKey { get; private set; }

        [JsonProperty]
        public DateTime StartTime { get; private set; }

        [JsonProperty]
        public DateTime FirstRunTime { get; private set; }

        [JsonProperty]
        public TaskInfo TaskInfo { get; private set; }
        
        [JsonProperty]
        public TimeSpan? Interval { get; private set; }
        
        [JsonProperty]
        public string Crontab { get; private set; }

        public RecurringTask() { }

        public RecurringTask(
            TaskInfo taskInfo,
            TimeSpan interval,
            TaskKey taskKey) : this(taskInfo, taskKey)
        {
            Interval = interval;

            FirstRunTime = GetNextRunTime(DateTime.UtcNow);
        }

        public RecurringTask(
            TaskInfo taskInfo,
            string crontab,
            TaskKey taskKey) : this(taskInfo, taskKey)
        {
            ValidateCrontab(crontab);
            Crontab = crontab;

            FirstRunTime = GetNextRunTime(DateTime.UtcNow);
        }

        private RecurringTask(TaskInfo taskInfo, TaskKey taskKey)
        {
            TaskInfo = taskInfo;
            StartTime = DateTime.UtcNow;

            TaskKey = taskKey;
        }

        public DateTime GetNextRunTime(DateTime baseTime)
        {
            if (baseTime == null) 
            {
                throw new ArgumentException("baseTime must not be null.");
            }
            
            if (Interval != null) 
            {
                if (baseTime < StartTime) 
                {
                    throw new ArgumentException("baseTime cannot be less than task StartTime");
                }

                var difference = (baseTime - StartTime).TotalMilliseconds;
                var intervalMs = Interval.Value.TotalMilliseconds;

                var elapsedIntervals = difference / intervalMs;
                var targetOffsetMs = (elapsedIntervals + 1) * intervalMs;

                var offsetFromBase = TimeSpan.FromMilliseconds(targetOffsetMs - difference);

                return (baseTime + offsetFromBase);
            }

            if (Crontab != null) 
            {
                var crontabSchedule = CrontabSchedule.Parse(Crontab, new CrontabSchedule.ParseOptions() { IncludingSeconds = true });

                var nextOccurence = crontabSchedule.GetNextOccurrence(baseTime);

                return nextOccurence;
            }

            throw new Exception("Crontab and Interval both null. Unexpected condition.");
        }

        public long GetNextRunTimestamp(DateTime baseTime)
        {
            return GetNextRunTime(baseTime).ToUnixTimeMilliseconds();
        }

        private void ValidateCrontab(string crontab)
        {
            try
            {
                var schedule = CrontabSchedule.Parse(crontab, new CrontabSchedule.ParseOptions{IncludingSeconds = true});
                schedule.GetNextOccurrence(DateTime.UtcNow);
            }
            catch (Exception ex)
            {
                throw new Exception("Crontab is invalid. See the inner exception for details.", ex);
            }
        }

        public bool IsEquivalent(RecurringTask comparisonRecurringTask)
        {
            if (comparisonRecurringTask == null) 
            {
                throw new ArgumentException("comparisonRecurringTask must not be null");
            }

            if (!comparisonRecurringTask.TaskKey.Equals(TaskKey))
            {
                throw new Exception("Cannot compare with a recurringTask with a different TaskKey.");
            }

            return TaskInfo.IsEquivalent(comparisonRecurringTask.TaskInfo)
                && Interval == comparisonRecurringTask.Interval
                && string.Equals(Crontab, comparisonRecurringTask.Crontab, StringComparison.Ordinal);
        }
    }
}