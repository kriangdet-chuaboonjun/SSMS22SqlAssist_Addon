using System;
using System.Globalization;
using System.Xml.Linq;
using SqlAssist.Core.Formatting;
using SqlAssist.Core.Risk;

namespace SqlAssist.Core.Settings
{
    /// <summary>What the user can set, kept in %APPDATA%\SqlAssist\settings.xml. The file is read as leniently as it is
    /// written: whatever is missing, misspelled or out of range falls back to the default for that one value, and a file that is
    /// not XML at all is the same as no file.</summary>
    public sealed class SqlAssistSettings
    {
        public const string FileName = "settings.xml";

        public FormatOptions Format { get; } = new FormatOptions();
        public RiskOptions Risk { get; } = new RiskOptions();
        public EditorOptions Editor { get; } = new EditorOptions();

        public static SqlAssistSettings Parse(string xml)
        {
            var settings = new SqlAssistSettings();
            XElement root;
            try
            {
                if (string.IsNullOrWhiteSpace(xml)) return settings;
                root = XDocument.Parse(xml).Root;
            }
            catch (System.Xml.XmlException)
            {
                return settings;
            }
            if (root == null || !Is(root, "SqlAssistSettings")) return settings;

            var format = Child(root, "Format");
            if (format != null)
            {
                if (Enum.TryParse(Attribute(format, "KeywordCase"), true, out KeywordCase keywordCase) && Enum.IsDefined(typeof(KeywordCase), keywordCase))
                    settings.Format.KeywordCase = keywordCase;
                if (Enum.TryParse(Attribute(format, "CommaStyle"), true, out CommaStyle commaStyle) && Enum.IsDefined(typeof(CommaStyle), commaStyle))
                    settings.Format.CommaStyle = commaStyle;
                if (int.TryParse(Attribute(format, "IndentSize"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int indent))
                    settings.Format.IndentSize = Math.Max(FormatOptions.MinIndentSize, Math.Min(FormatOptions.MaxIndentSize, indent));
            }

            var risk = Child(root, "RiskCheck");
            if (risk != null)
            {
                settings.Risk.UpdateWithoutWhere = Flag(risk, "UpdateWithoutWhere", settings.Risk.UpdateWithoutWhere);
                settings.Risk.DeleteWithoutWhere = Flag(risk, "DeleteWithoutWhere", settings.Risk.DeleteWithoutWhere);
                settings.Risk.TruncateTable = Flag(risk, "TruncateTable", settings.Risk.TruncateTable);
                settings.Risk.DropTable = Flag(risk, "DropTable", settings.Risk.DropTable);
                settings.Risk.SkipTempTables = Flag(risk, "SkipTempTables", settings.Risk.SkipTempTables);
                settings.Risk.WarnBeforeExecute = Flag(risk, "WarnBeforeExecute", settings.Risk.WarnBeforeExecute);
            }

            var editor = Child(root, "Editor");
            if (editor != null)
                settings.Editor.HighlightInsertColumns = Flag(editor, "HighlightInsertColumns", settings.Editor.HighlightInsertColumns);
            return settings;
        }

        public string ToXml()
        {
            var document = new XDocument(
                new XElement("SqlAssistSettings",
                    new XElement("Format",
                        new XAttribute("KeywordCase", Format.KeywordCase.ToString()),
                        new XAttribute("IndentSize", Format.IndentSize.ToString(CultureInfo.InvariantCulture)),
                        new XAttribute("CommaStyle", Format.CommaStyle.ToString())),
                    new XElement("Editor",
                        new XAttribute("HighlightInsertColumns", Editor.HighlightInsertColumns)),
                    new XElement("RiskCheck",
                        new XAttribute("UpdateWithoutWhere", Risk.UpdateWithoutWhere),
                        new XAttribute("DeleteWithoutWhere", Risk.DeleteWithoutWhere),
                        new XAttribute("TruncateTable", Risk.TruncateTable),
                        new XAttribute("DropTable", Risk.DropTable),
                        new XAttribute("SkipTempTables", Risk.SkipTempTables),
                        new XAttribute("WarnBeforeExecute", Risk.WarnBeforeExecute))));
            return document.Declaration == null ? document.ToString() : document.Declaration + Environment.NewLine + document;
        }

        /// <summary>"true" or "false" in any case; anything else leaves the default.</summary>
        private static bool Flag(XElement element, string name, bool fallback)
        {
            string value = Attribute(element, name);
            if (string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(value, "false", StringComparison.OrdinalIgnoreCase)) return false;
            return fallback;
        }

        private static bool Is(XElement element, string name) => string.Equals(element.Name.LocalName, name, StringComparison.OrdinalIgnoreCase);

        private static XElement Child(XElement parent, string name)
        {
            foreach (var child in parent.Elements())
                if (Is(child, name)) return child;
            return null;
        }

        private static string Attribute(XElement element, string name)
        {
            foreach (var attribute in element.Attributes())
                if (string.Equals(attribute.Name.LocalName, name, StringComparison.OrdinalIgnoreCase)) return attribute.Value;
            return null;
        }
    }
}
