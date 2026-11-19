namespace Axis.Configuration.Model;

/// <summary>A site's record page: the page that opens the records of an entity.</summary>
public sealed record SiteRecordPageModel(EntityReference Entity, PageReference Page);
