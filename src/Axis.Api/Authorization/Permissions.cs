namespace Axis.Api.Authorization;

public static class Permissions
{
    public static class DataModeling
    {
        public const string ModelRead = "data_modeling:model:read";
        public const string ModelWrite = "data_modeling:model:write";
        public const string ModelDelete = "data_modeling:model:delete";
        public const string RecordRead = "data_modeling:record:read";
        public const string RecordWrite = "data_modeling:record:write";
        public const string RecordDelete = "data_modeling:record:delete";
    }

    public static class Workflow
    {
        public const string DefinitionRead = "workflow:definition:read";
        public const string DefinitionWrite = "workflow:definition:write";
        public const string DefinitionDelete = "workflow:definition:delete";
        public const string TriggerManual = "workflow:trigger:manual";
    }

    public static class Form
    {
        public const string DefinitionRead = "form:definition:read";
        public const string DefinitionWrite = "form:definition:write";
        public const string Submit = "form:submit";
    }

    public static class Execution
    {
        public const string Read = "execution:read";
        public const string Cancel = "execution:cancel";
        public const string Retry = "execution:retry";
    }

    public static class Page
    {
        public const string Read = "page:read";
        public const string Write = "page:write";
        public const string Publish = "page:publish";
    }

    public static class Users
    {
        public const string Read = "users:read";
        public const string Invite = "users:invite";
        public const string Deactivate = "users:deactivate";
    }

    public static class Roles
    {
        public const string Read = "roles:read";
        public const string Write = "roles:write";
    }

    public static class Organization
    {
        public const string SettingsRead = "organization:settings:read";
        public const string SettingsWrite = "organization:settings:write";
        public const string Delete = "organization:delete";
    }
}
