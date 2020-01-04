using System;

namespace Gofer.NET
{
    public class TaskKey
    {
        public string Value { get; set; }

        public string RecurringTaskName { get; set; }

        public static TaskKey CreateUnique()
        {
            return new TaskKey($"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}::{Guid.NewGuid().ToString()}");
        }

        public static TaskKey CreateRecurring(string recurringTaskName)
        {
            return new TaskKey($"Recurring{nameof(TaskKey)}::{recurringTaskName}", recurringTaskName);
        }

        public TaskKey() {}

        public TaskKey(string value)
        {
            Value = value;
            RecurringTaskName = null;
        }

        public TaskKey(string value, string recurringTaskName)
        {
            Value = value;
            RecurringTaskName = recurringTaskName;
        }

        public override bool Equals(object obj)
        {
            if (obj == null || GetType() != obj.GetType())
            {
                return false;
            }

            return string.Equals(Value, ((TaskKey) obj).Value);
        }
        
        public override int GetHashCode()
        {
            return Value.GetHashCode();
        }

        public override string ToString()
        {
            return Value;
        }
    }
}