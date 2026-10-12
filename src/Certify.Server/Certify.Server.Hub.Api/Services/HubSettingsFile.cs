using System.Text.Json;
using System.Text.Json.Nodes;

namespace Certify.Server.Hub.Api.Services
{
    /// <summary>
    /// Reads and rewrites the hub settings file (hubservice.json) as a JSON document.
    ///
    /// Rewriting the document does not preserve comments, so the previous file is copied alongside first. Callers which
    /// can make their change as a text replacement should prefer that, as it keeps the comments in the shipped defaults.
    /// </summary>
    public static class HubSettingsFile
    {
        private static readonly JsonDocumentOptions DocumentOptions = new()
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        /// <summary>
        /// Parse settings file content, returning null when it is not a JSON object.
        /// </summary>
        public static JsonObject? Parse(string content) => JsonNode.Parse(content, documentOptions: DocumentOptions) as JsonObject;

        /// <summary>
        /// Get a top level section, adding it when absent.
        /// </summary>
        public static JsonObject EnsureSection(JsonObject root, string sectionName)
        {
            if (root[sectionName] is JsonObject section)
            {
                return section;
            }

            section = new JsonObject();
            root[sectionName] = section;
            return section;
        }

        /// <summary>
        /// Write the settings document, first copying the previous file to a .bak alongside it.
        /// </summary>
        /// <returns>The backup path, or null when the backup could not be made.</returns>
        public static string? WriteWithBackup(string configPath, JsonObject root)
        {
            var backupPath = configPath + ".bak";

            try
            {
                File.Copy(configPath, backupPath, overwrite: true);
            }
            catch (Exception)
            {
                // best effort, losing the backup is not a reason to abandon the change
                backupPath = null;
            }

            File.WriteAllText(configPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

            return backupPath;
        }
    }
}
