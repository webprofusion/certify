using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Certify.Models.Config.Migration;
using Newtonsoft.Json;

namespace Certify.CLI
{
    public partial class CertifyCLI
    {
        public async Task PerformBackupExport(string[] args)
        {
            InitPlugins();

            var filename = args[args.Length - 2];
            var secret = args[args.Length - 1];

            if (Directory.Exists(filename))
            {
                filename = Path.Combine(filename, $"certifytheweb_export_{DateTime.Now.ToString("yyyyMMdd")}.json");
            }

            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("Performing export: " + filename);

            var exportRequest = new ExportRequest { IsPreviewMode = false, Settings = new ExportSettings { EncryptionSecret = secret, ExportAllStoredCredentials = true } };

            var export = await _certifyClient.PerformExport(exportRequest);

            System.IO.File.WriteAllText(filename, JsonConvert.SerializeObject(export));

            foreach (var e in export.Errors)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Export error: {e}");
            }

            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine($"Export completed with {export.Errors.Count} errors");
        }

        public async Task PerformBackupImport(string[] args)
        {
            InitPlugins();

            var filename = args[args.Length - 2];
            var secret = args[args.Length - 1];

            var isPreview = args.Contains("preview");

            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("Performing import: " + filename);

            var json = System.IO.File.ReadAllText(filename);
            var importRequest = new ImportRequest { IsPreviewMode = isPreview, Settings = new ImportSettings { EncryptionSecret = secret } };
            try
            {
                importRequest.Package = JsonConvert.DeserializeObject<ImportExportPackage>(json);
            }
            catch (Exception)
            {
                Console.WriteLine("The selected file could not be read as a valid Import Package.");
                return;
            }

            var importSteps = await _certifyClient.PerformImport(importRequest);

            foreach (var s in importSteps)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                if (s.HasError)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"{s.Description}");
                }
                else if (s.HasWarning)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                }

                Console.WriteLine($"{s.Title} [{s.Substeps?.Count}]");
            }

            Console.WriteLine($"Import completed with {importSteps.Count(i => i.HasError == true)} errors");
        }
    }
}
