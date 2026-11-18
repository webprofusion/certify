using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Certify.Models;
using Certify.Models.Config.Migration;

namespace Certify.UI.Windows
{
    /// <summary>
    /// A finding of a migration preview, with the input chosen for it where it takes one
    /// </summary>
    public class MigrationFindingRow : BindableBase
    {
        public MigrationFindingRow(ExternalMigrationFinding finding, List<ExternalMigrationInputOption> options)
        {
            Finding = finding;
            Options = options;
        }

        public ExternalMigrationFinding Finding { get; }
        public List<ExternalMigrationInputOption> Options { get; }
        public string Input { get; set; }

        public string Heading => $"{Finding.Category}: {Finding.Title}.";

        public string LevelTitle => Finding.Level switch
        {
            ExternalMigrationFindingLevel.Blocker => Finding.IsUnresolved ? "Blocker" : "Resolved",
            ExternalMigrationFindingLevel.InputNeeded => Finding.IsUnresolved ? "Input needed" : "Resolved",
            ExternalMigrationFindingLevel.Assumption => "Assumption",
            _ => "Mapped"
        };

        public string Glyph => Finding.IsUnresolved ? "✖" : Finding.Level == ExternalMigrationFindingLevel.Assumption ? "⚠" : "✔";

        public Brush GlyphBrush => Finding.IsUnresolved ? Brushes.Crimson : Finding.Level == ExternalMigrationFindingLevel.Assumption ? Brushes.DarkOrange : Brushes.SeaGreen;

        public bool TakesInput => Finding.InputType == ExternalMigrationInputTypes.Credential || Finding.InputType == ExternalMigrationInputTypes.Choice;
        public Visibility InputVisibility => TakesInput ? Visibility.Visible : Visibility.Collapsed;
        public string InputPrompt => Finding.InputType == ExternalMigrationInputTypes.Credential
            ? (Options.Any() ? "Credential to use instead" : "No suitable stored credential yet, add one then preview again")
            : "Choose";
    }

    /// <summary>
    /// Previews and imports a certificate managed by another certificate manager, then takes the imported certificate
    /// through test, cut over, roll back and removal of the source
    /// </summary>
    public partial class ExternalMigrationWindow
    {
        protected Certify.UI.ViewModel.AppViewModel MainViewModel => ViewModel.AppViewModel.Current;

        private ManagedCertificate _item;
        private ExternalMigrationPlan _plan;
        private List<MigrationFindingRow> _rows = new List<MigrationFindingRow>();
        private bool _busy;

        public ExternalMigrationWindow(ManagedCertificate item)
        {
            _item = item;

            InitializeComponent();
            Width *= MainViewModel.UIScaleFactor;
            Height *= MainViewModel.UIScaleFactor;

            UpdateView();
        }

        private bool IsExternal => _item.IsExternallyManaged;
        private ExternalMigrationInfo Info => _item.MigrationInfo;

        private void UpdateView()
        {
            Heading.Text = _item.Name;

            var state = Info?.State;

            Stage.Text = Info == null
                ? "Preview what would be created here. Nothing changes in the other certificate manager until you cut over after testing."
                : StageDescription(Info) + (Info.LastTestPassed == false && !string.IsNullOrWhiteSpace(Info.LastTestMessage) ? $" Last test: {Info.LastTestMessage}" : "");

            Summary.Text = _plan?.Summary ?? "";
            Findings.ItemsSource = null;
            Findings.ItemsSource = _rows;
            Secrets.Text = _plan?.Secrets.Any() == true
                ? $"Secrets read from {_plan.SourceProviderTitle} at import and stored as credentials on this instance: " + string.Join(", ", _plan.Secrets.Select(s => s.IsReadable ? s.Label : $"{s.Label} (not readable: {s.Reason})"))
                : "";

            Show(Preview, IsExternal);
            Show(ImportOptions, IsExternal && _plan != null);
            Show(Import, IsExternal && _plan != null);
            Import.IsEnabled = !_busy && _plan?.CanImport == true;

            Show(Test, state == ExternalMigrationState.Imported || state == ExternalMigrationState.Tested || state == ExternalMigrationState.RolledBack);
            Show(CutOver, state == ExternalMigrationState.Tested && Info.LastTestPassed == true);
            Show(Rollback, Info?.IsSourceHidden == true);
            Show(RemoveSource, Info?.IsSourceHidden == true);
            Show(ConfirmUnverified, state == ExternalMigrationState.CutOver);

            foreach (var b in new[] { Preview, Test, CutOver, Rollback, RemoveSource })
            {
                b.IsEnabled = !_busy;
            }

            Progress.IsActive = _busy;
            Show(Progress, _busy);
        }

        private static void Show(UIElement element, bool visible) => element.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

        private static string StageDescription(ExternalMigrationInfo info) => info.State switch
        {
            ExternalMigrationState.Imported => $"Imported with auto renewal off. {info.SourceProviderTitle} still renews this certificate. Test the migration next.",
            ExternalMigrationState.Tested => $"Tests passed. {info.SourceProviderTitle} still renews this certificate until you cut over, which disables it there and turns on auto renewal here.",
            ExternalMigrationState.CutOver => $"This instance renews the certificate and {info.SourceProviderTitle}'s renewal is disabled. The first successful renewal here verifies the migration.",
            ExternalMigrationState.Verified => $"This instance has renewed the certificate since cut over. The {info.SourceProviderTitle} renewal can now be removed.",
            ExternalMigrationState.RolledBack => $"Rolled back. {info.SourceProviderTitle} renews the certificate again and auto renewal is off here.",
            ExternalMigrationState.SourceRemoved => $"The {info.SourceProviderTitle} renewal has been removed. Migration is complete.",
            _ => ""
        };

        private Dictionary<string, string> CurrentInputs() => _rows
            .Where(r => r.TakesInput && !string.IsNullOrWhiteSpace(r.Input))
            .ToDictionary(r => r.Finding.Key, r => r.Input);

        private async Task Run(Func<Task> action)
        {
            _busy = true;
            UpdateView();

            try
            {
                await action();
            }
            catch (Exception exp)
            {
                ShowText($"The request failed: {exp.Message}");
            }
            finally
            {
                _busy = false;
                UpdateView();
            }
        }

        private void ShowText(string text)
        {
            ResultText.Text = text;
            Show(ResultText, !string.IsNullOrWhiteSpace(text));
        }

        private void ShowResult(ExternalMigrationResult result)
        {
            var text = new StringBuilder(result.Message);

            foreach (var step in result.Steps ?? new List<ActionStep>())
            {
                AppendStep(text, step, 0);
            }

            ShowText(text.ToString());
        }

        private static void AppendStep(StringBuilder text, ActionStep step, int depth)
        {
            text.AppendLine();
            text.Append(new string(' ', depth * 4));
            text.Append(string.IsNullOrWhiteSpace(step.Description) ? step.Title : $"{step.Title}: {step.Description}");

            foreach (var sub in step.Substeps ?? new List<ActionStep>())
            {
                AppendStep(text, sub, depth + 1);
            }
        }

        /// <summary>
        /// Stages after import act on the imported item, so reload it to show its new state
        /// </summary>
        private void ReloadItem(string id)
        {
            var updated = MainViewModel.ManagedCertificates.FirstOrDefault(m => m.Id == id);

            if (updated != null)
            {
                _item = updated;
            }
        }

        private async void Preview_Click(object sender, RoutedEventArgs e) => await RunPreview();

        /// <summary>
        /// A changed input is applied by preparing the plan again, so the descriptions and the proposed item follow it
        /// </summary>
        private async void Input_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var row = (sender as FrameworkElement)?.DataContext as MigrationFindingRow;
            var option = e.AddedItems.Count > 0 ? e.AddedItems[0] as ExternalMigrationInputOption : null;

            if (_busy || row == null || option == null || option.Value == row.Finding.AppliedInput)
            {
                return;
            }

            row.Input = option.Value;
            await RunPreview();
        }

        private Task RunPreview() => Run(async () =>
        {
            await MainViewModel.RefreshStoredCredentialsList();

            _plan = await MainViewModel.PreviewExternalItemMigration(_item.Id, new ExternalMigrationRequest { Inputs = CurrentInputs() });

            var previous = CurrentInputs();

            _rows = _plan.Findings
                .OrderBy(f => f.Level)
                .Select(f => new MigrationFindingRow(f, OptionsFor(f)) { Input = previous.TryGetValue(f.Key, out var v) ? v : f.AppliedInput })
                .ToList();

            ShowText(_plan.ExistingMigratedItemId != null ? "This certificate has already been imported. Continue the migration from the imported certificate." : "");
        });

        private List<ExternalMigrationInputOption> OptionsFor(ExternalMigrationFinding finding)
        {
            if (finding.InputType == ExternalMigrationInputTypes.Credential)
            {
                return MainViewModel.StoredCredentials
                    .Where(c => finding.CredentialType == null || c.ProviderType == finding.CredentialType)
                    .OrderBy(c => c.Title)
                    .Select(c => new ExternalMigrationInputOption { Value = c.StorageKey, Label = c.Title })
                    .ToList();
            }

            return finding.Options ?? new List<ExternalMigrationInputOption>();
        }

        private async void Import_Click(object sender, RoutedEventArgs e)
        {
            if (_plan.HasAssumptions && Acknowledge.IsChecked != true)
            {
                MessageBox.Show("Review the assumptions, then tick to accept them before importing.", "Migrate Certificate");
                return;
            }

            await Run(async () =>
            {
                var result = await MainViewModel.PerformExternalItemImport(_item.Id, new ExternalMigrationRequest
                {
                    SourceFileHash = _plan.SourceFileHash,
                    Inputs = CurrentInputs(),
                    AcknowledgeAssumptions = Acknowledge.IsChecked == true,
                    AdoptCurrentCertificate = AdoptCurrent.IsChecked == true
                });

                ShowResult(result);

                if (result.IsSuccess && result.ManagedCertificateId != null)
                {
                    // from here on the window follows the imported certificate
                    _plan = null;
                    _rows = new List<MigrationFindingRow>();
                    ReloadItem(result.ManagedCertificateId);
                }
            });
        }

        private async Task RunStage(Func<Task<ExternalMigrationResult>> stage) => await Run(async () =>
        {
            var id = _item.Id;
            var result = await stage();
            ShowResult(result);
            ReloadItem(id);
        });

        private async void Test_Click(object sender, RoutedEventArgs e) => await RunStage(() => MainViewModel.TestExternalItemMigration(_item.Id));

        private async void CutOver_Click(object sender, RoutedEventArgs e)
        {
            var related = Info.RelatedSourceItemIds.Any() ? $" The {Info.RelatedSourceItemIds.Count} other certificate(s) renewed by the same entry are cut over with it." : "";

            if (MessageBox.Show($"Disable this certificate's renewal in {Info.SourceProviderTitle} and turn on auto renewal here?{related}", "Cut Over", MessageBoxButton.OKCancel) == MessageBoxResult.OK)
            {
                await RunStage(() => MainViewModel.CutOverExternalItemMigration(_item.Id));
            }
        }

        private async void Rollback_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show($"Restore renewal in {Info.SourceProviderTitle} and turn off auto renewal here?", "Roll Back", MessageBoxButton.OKCancel) == MessageBoxResult.OK)
            {
                await RunStage(() => MainViewModel.RollbackExternalItemMigration(_item.Id));
            }
        }

        private async void RemoveSource_Click(object sender, RoutedEventArgs e)
        {
            if (Info.State == ExternalMigrationState.CutOver && ConfirmUnverified.IsChecked != true)
            {
                MessageBox.Show("No renewal has succeeded here since cut over. Tick to confirm removal of the source anyway.", "Remove Source");
                return;
            }

            var request = new ExternalMigrationActionRequest { PreviewOnly = true, ConfirmUnverified = ConfirmUnverified.IsChecked == true };

            ExternalMigrationResult preview = null;
            await Run(async () => preview = await MainViewModel.RemoveExternalItemSource(_item.Id, request));

            if (preview == null)
            {
                return;
            }

            ShowResult(preview);

            if (!preview.IsSuccess)
            {
                return;
            }

            if (MessageBox.Show($"Remove this certificate's renewal from {Info.SourceProviderTitle}, as listed? This cannot be undone.", "Remove Source", MessageBoxButton.OKCancel, MessageBoxImage.Warning) == MessageBoxResult.OK)
            {
                request.PreviewOnly = false;
                await RunStage(() => MainViewModel.RemoveExternalItemSource(_item.Id, request));
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
