namespace lucidRESUME.Core.Models.Profile;

public static class ProfileSchemaDefaults
{
    public static ProfileFormDefinition Create() => new()
    {
        SchemaVersion = "1.0",
        Groups =
        [
            new ProfileFieldGroup
            {
                GroupId = "identity",
                DisplayName = "Identity",
                AccentColor = "#89B4FA",
                Icon = "👤",
                DisplayOrder = 0,
                Fields =
                [
                    new ProfileField
                    {
                        FieldId = "display-name",
                        DisplayName = "Your Name",
                        DataType = ProfileFieldDataType.Text,
                        DisplayOrder = 0,
                        CvMappingHint = "person_name"
                    },
                    new ProfileField
                    {
                        FieldId = "current-title",
                        DisplayName = "Current Job Title",
                        DataType = ProfileFieldDataType.Text,
                        DisplayOrder = 1,
                        CvMappingHint = "job_title"
                    },
                    new ProfileField
                    {
                        FieldId = "years-experience",
                        DisplayName = "Years of Experience",
                        DataType = ProfileFieldDataType.Number,
                        DisplayOrder = 2,
                        CvMappingHint = "years_experience"
                    }
                ]
            },
            new ProfileFieldGroup
            {
                GroupId = "work-prefs",
                DisplayName = "Work Preferences",
                AccentColor = "#CBA6F7",
                Icon = "💼",
                DisplayOrder = 1,
                Fields =
                [
                    new ProfileField
                    {
                        FieldId = "work-style",
                        DisplayName = "Work Style",
                        DataType = ProfileFieldDataType.WorkStyleToggles,
                        DisplayOrder = 0
                    },
                    new ProfileField
                    {
                        FieldId = "preferred-locations",
                        DisplayName = "Preferred Locations",
                        DataType = ProfileFieldDataType.TagList,
                        DisplayOrder = 1
                    },
                    new ProfileField
                    {
                        FieldId = "target-roles",
                        DisplayName = "Target Roles",
                        DataType = ProfileFieldDataType.TagList,
                        DisplayOrder = 2
                    },
                    new ProfileField
                    {
                        FieldId = "min-salary",
                        DisplayName = "Minimum Salary",
                        DataType = ProfileFieldDataType.Currency,
                        DisplayOrder = 3
                    },
                    new ProfileField
                    {
                        FieldId = "max-commute",
                        DisplayName = "Max Commute (minutes)",
                        DataType = ProfileFieldDataType.Number,
                        DisplayOrder = 4
                    },
                    new ProfileField
                    {
                        FieldId = "target-industries",
                        DisplayName = "Target Industries",
                        DataType = ProfileFieldDataType.TagList,
                        DisplayOrder = 5
                    },
                    new ProfileField
                    {
                        FieldId = "blocked-industries",
                        DisplayName = "Blocked Industries",
                        DataType = ProfileFieldDataType.TagList,
                        DisplayOrder = 6
                    }
                ]
            },
            new ProfileFieldGroup
            {
                GroupId = "skills",
                DisplayName = "Skills",
                AccentColor = "#A6E3A1",
                Icon = "⚡",
                DisplayOrder = 2,
                Fields =
                [
                    new ProfileField
                    {
                        FieldId = "skills-emphasise",
                        DisplayName = "Skills to Emphasise",
                        DataType = ProfileFieldDataType.TagListWithReason,
                        DisplayOrder = 0
                    },
                    new ProfileField
                    {
                        FieldId = "skills-avoid",
                        DisplayName = "Skills to Avoid",
                        DataType = ProfileFieldDataType.TagListWithReason,
                        DisplayOrder = 1
                    }
                ]
            },
            new ProfileFieldGroup
            {
                GroupId = "ai-context",
                DisplayName = "AI Context",
                AccentColor = "#F9E2AF",
                Icon = "🤖",
                DisplayOrder = 3,
                Fields =
                [
                    new ProfileField
                    {
                        FieldId = "career-goals",
                        DisplayName = "Career Goals",
                        DataType = ProfileFieldDataType.LongText,
                        DisplayOrder = 0
                    },
                    new ProfileField
                    {
                        FieldId = "additional-context",
                        DisplayName = "Additional Context",
                        DataType = ProfileFieldDataType.LongText,
                        DisplayOrder = 1
                    },
                    new ProfileField
                    {
                        FieldId = "blocked-companies",
                        DisplayName = "Blocked Companies",
                        DataType = ProfileFieldDataType.TagList,
                        DisplayOrder = 2
                    }
                ]
            }
        ]
    };
}
