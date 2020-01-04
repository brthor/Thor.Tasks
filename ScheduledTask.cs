using System;
using System.Threading.Tasks;
using Gofer.NET.Utils;
using NCrontab;
using Newtonsoft.Json;

namespace Gofer.NET
{
    [JsonObject(MemberSerialization.OptIn)]
    public class ScheduledTask
    {
        public string LockKey => $"{nameof(ScheduledTask)}::{TaskKey}::ScheduleLock";

        [JsonProperty]
        public TaskKey TaskKey { get; private set; }

        [JsonProperty]
        public long ScheduledUnixTimeMilliseconds { get; private set; }

        [JsonProperty]
        public TaskInfo TaskInfo { get; private set; }
        
        public ScheduledTask() { }

        public ScheduledTask(
            TaskInfo taskInfo,
            TimeSpan offset,
            TaskKey taskKey) : this(taskInfo, new DateTimeOffset(DateTime.UtcNow + offset), taskKey)
        {
        }

        public ScheduledTask(
            TaskInfo taskInfo,
            DateTime scheduledTime,
            TaskQueue taskQueue,
            TaskKey taskKey) : this(taskInfo, new DateTimeOffset(scheduledTime), taskKey)
        {
        }

        public ScheduledTask(
            TaskInfo taskInfo,
            DateTimeOffset scheduledDateTimeOffset,
            TaskKey taskKey)
        {
            TaskKey = taskKey;
            TaskInfo = taskInfo;
            ScheduledUnixTimeMilliseconds = scheduledDateTimeOffset.ToUnixTimeMilliseconds();
        }
    }
}