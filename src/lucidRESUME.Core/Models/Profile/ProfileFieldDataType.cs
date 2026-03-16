namespace lucidRESUME.Core.Models.Profile;

public enum ProfileFieldDataType
{
    Text,               // single-line text
    LongText,           // multiline textarea
    Number,             // integer (e.g. years experience)
    Currency,           // decimal + currency code
    Boolean,            // single checkbox
    TagList,            // ObservableCollection<TagItem>, no reasons
    TagListWithReason,  // ObservableCollection<TagItem>, reasons supported
    WorkStyleToggles,   // remote/hybrid/onsite booleans
}
