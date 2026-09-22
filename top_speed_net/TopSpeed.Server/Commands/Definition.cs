using System;

namespace TopSpeed.Server.Commands
{
    internal sealed class CommandDefinition
    {
        private readonly Action<string> _execute;

        public CommandDefinition(string name, string description, Action execute)
            : this(name, description, WithoutArguments(execute))
        {
        }

        /// <summary>
        /// For commands that take options. Everything after the command name is passed through
        /// untouched; most commands do not care and use the parameterless form.
        /// </summary>
        public CommandDefinition(string name, string description, Action<string> execute)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Command name is required.", nameof(name));
            if (string.IsNullOrWhiteSpace(description))
                throw new ArgumentException("Command description is required.", nameof(description));

            Name = name.Trim();
            Description = description.Trim();
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        }

        public string Name { get; }
        public string Description { get; }

        public void Execute(string arguments = "")
        {
            _execute(arguments ?? string.Empty);
        }

        private static Action<string> WithoutArguments(Action execute)
        {
            if (execute == null)
                throw new ArgumentNullException(nameof(execute));

            return _ => execute();
        }
    }
}
