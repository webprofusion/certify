namespace Certify
{
    partial class MainForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(MainForm));
            this.menuStrip1 = new System.Windows.Forms.MenuStrip();
            this.toolStripMenuItem1 = new System.Windows.Forms.ToolStripMenuItem();
            this.newToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.contactRegistrationToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.domainCertificateToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripSeparator3 = new System.Windows.Forms.ToolStripSeparator();
            this.reloadVaultToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripSeparator1 = new System.Windows.Forms.ToolStripSeparator();
            this.fileToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.optionsToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.settingsToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.cleanupVaultToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.helpToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.websiteToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripSeparator4 = new System.Windows.Forms.ToolStripSeparator();
            this.checkForUpdatesToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripSeparator2 = new System.Windows.Forms.ToolStripSeparator();
            this.aboutToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStrip1 = new System.Windows.Forms.ToolStrip();
            this.toolStripButtonNewContact = new System.Windows.Forms.ToolStripButton();
            this.toolStripButtonNewCertificate = new System.Windows.Forms.ToolStripButton();
            this.tabControl1 = new System.Windows.Forms.TabControl();
            this.tabPageManagedSites = new System.Windows.Forms.TabPage();
            this.managedSites1 = new Certify.Forms.Controls.ManagedSites();
            this.tabPageVault = new System.Windows.Forms.TabPage();
            this.tabPageLog = new System.Windows.Forms.TabPage();
            this.txtOutput = new System.Windows.Forms.TextBox();
            this.vaultExplorer1 = new Certify.Forms.Controls.VaultExplorer();
            this.menuStrip1.SuspendLayout();
            this.toolStrip1.SuspendLayout();
            this.tabControl1.SuspendLayout();
            this.tabPageManagedSites.SuspendLayout();
            this.tabPageVault.SuspendLayout();
            this.tabPageLog.SuspendLayout();
            this.SuspendLayout();
            // 
            // menuStrip1
            // 
            this.menuStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.toolStripMenuItem1,
            this.optionsToolStripMenuItem,
            this.helpToolStripMenuItem});
            this.menuStrip1.Location = new System.Drawing.Point(0, 0);
            this.menuStrip1.Name = "menuStrip1";
            this.menuStrip1.Size = new System.Drawing.Size(784, 24);
            this.menuStrip1.TabIndex = 0;
            this.menuStrip1.Text = "menuStrip1";
            // 
            // toolStripMenuItem1
            // 
            this.toolStripMenuItem1.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.newToolStripMenuItem,
            this.toolStripSeparator3,
            this.reloadVaultToolStripMenuItem,
            this.toolStripSeparator1,
            this.fileToolStripMenuItem});
            this.toolStripMenuItem1.Name = "toolStripMenuItem1";
            this.toolStripMenuItem1.Size = new System.Drawing.Size(37, 20);
            this.toolStripMenuItem1.Text = "File";
            // 
            // newToolStripMenuItem
            // 
            this.newToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.contactRegistrationToolStripMenuItem,
            this.domainCertificateToolStripMenuItem});
            this.newToolStripMenuItem.Name = "newToolStripMenuItem";
            this.newToolStripMenuItem.Size = new System.Drawing.Size(139, 22);
            this.newToolStripMenuItem.Text = "New..";
            // 
            // contactRegistrationToolStripMenuItem
            // 
            this.contactRegistrationToolStripMenuItem.Name = "contactRegistrationToolStripMenuItem";
            this.contactRegistrationToolStripMenuItem.Size = new System.Drawing.Size(191, 22);
            this.contactRegistrationToolStripMenuItem.Text = "Contact Registration...";
            this.contactRegistrationToolStripMenuItem.Click += new System.EventHandler(this.contactRegistrationToolStripMenuItem_Click);
            // 
            // domainCertificateToolStripMenuItem
            // 
            this.domainCertificateToolStripMenuItem.Name = "domainCertificateToolStripMenuItem";
            this.domainCertificateToolStripMenuItem.Size = new System.Drawing.Size(191, 22);
            this.domainCertificateToolStripMenuItem.Text = "Domain Certificate...";
            this.domainCertificateToolStripMenuItem.Click += new System.EventHandler(this.domainCertificateToolStripMenuItem_Click);
            // 
            // toolStripSeparator3
            // 
            this.toolStripSeparator3.Name = "toolStripSeparator3";
            this.toolStripSeparator3.Size = new System.Drawing.Size(136, 6);
            // 
            // reloadVaultToolStripMenuItem
            // 
            this.reloadVaultToolStripMenuItem.Name = "reloadVaultToolStripMenuItem";
            this.reloadVaultToolStripMenuItem.Size = new System.Drawing.Size(139, 22);
            this.reloadVaultToolStripMenuItem.Text = "Reload Vault";
            this.reloadVaultToolStripMenuItem.Click += new System.EventHandler(this.reloadVaultToolStripMenuItem_Click);
            // 
            // toolStripSeparator1
            // 
            this.toolStripSeparator1.Name = "toolStripSeparator1";
            this.toolStripSeparator1.Size = new System.Drawing.Size(136, 6);
            // 
            // fileToolStripMenuItem
            // 
            this.fileToolStripMenuItem.Name = "fileToolStripMenuItem";
            this.fileToolStripMenuItem.Size = new System.Drawing.Size(139, 22);
            this.fileToolStripMenuItem.Text = "Exit";
            this.fileToolStripMenuItem.Click += new System.EventHandler(this.fileToolStripMenuItem_Click);
            // 
            // optionsToolStripMenuItem
            // 
            this.optionsToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.settingsToolStripMenuItem,
            this.cleanupVaultToolStripMenuItem});
            this.optionsToolStripMenuItem.Name = "optionsToolStripMenuItem";
            this.optionsToolStripMenuItem.Size = new System.Drawing.Size(61, 20);
            this.optionsToolStripMenuItem.Text = "Options";
            // 
            // settingsToolStripMenuItem
            // 
            this.settingsToolStripMenuItem.Name = "settingsToolStripMenuItem";
            this.settingsToolStripMenuItem.Size = new System.Drawing.Size(153, 22);
            this.settingsToolStripMenuItem.Text = "Settings..";
            this.settingsToolStripMenuItem.Click += new System.EventHandler(this.settingsToolStripMenuItem_Click);
            // 
            // cleanupVaultToolStripMenuItem
            // 
            this.cleanupVaultToolStripMenuItem.Name = "cleanupVaultToolStripMenuItem";
            this.cleanupVaultToolStripMenuItem.Size = new System.Drawing.Size(153, 22);
            this.cleanupVaultToolStripMenuItem.Text = "Cleanup Vault..";
            this.cleanupVaultToolStripMenuItem.Click += new System.EventHandler(this.cleanupVaultToolStripMenuItem_Click);
            // 
            // helpToolStripMenuItem
            // 
            this.helpToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.websiteToolStripMenuItem,
            this.toolStripSeparator4,
            this.checkForUpdatesToolStripMenuItem,
            this.toolStripSeparator2,
            this.aboutToolStripMenuItem});
            this.helpToolStripMenuItem.Name = "helpToolStripMenuItem";
            this.helpToolStripMenuItem.Size = new System.Drawing.Size(44, 20);
            this.helpToolStripMenuItem.Text = "Help";
            // 
            // websiteToolStripMenuItem
            // 
            this.websiteToolStripMenuItem.Name = "websiteToolStripMenuItem";
            this.websiteToolStripMenuItem.Size = new System.Drawing.Size(182, 22);
            this.websiteToolStripMenuItem.Text = "Support";
            this.websiteToolStripMenuItem.Click += new System.EventHandler(this.websiteToolStripMenuItem_Click);
            // 
            // toolStripSeparator4
            // 
            this.toolStripSeparator4.Name = "toolStripSeparator4";
            this.toolStripSeparator4.Size = new System.Drawing.Size(179, 6);
            // 
            // checkForUpdatesToolStripMenuItem
            // 
            this.checkForUpdatesToolStripMenuItem.Name = "checkForUpdatesToolStripMenuItem";
            this.checkForUpdatesToolStripMenuItem.Size = new System.Drawing.Size(182, 22);
            this.checkForUpdatesToolStripMenuItem.Text = "Check For Updates...";
            this.checkForUpdatesToolStripMenuItem.Click += new System.EventHandler(this.checkForUpdatesToolStripMenuItem_Click);
            // 
            // toolStripSeparator2
            // 
            this.toolStripSeparator2.Name = "toolStripSeparator2";
            this.toolStripSeparator2.Size = new System.Drawing.Size(179, 6);
            // 
            // aboutToolStripMenuItem
            // 
            this.aboutToolStripMenuItem.Name = "aboutToolStripMenuItem";
            this.aboutToolStripMenuItem.Size = new System.Drawing.Size(182, 22);
            this.aboutToolStripMenuItem.Text = "About";
            this.aboutToolStripMenuItem.Click += new System.EventHandler(this.aboutToolStripMenuItem_Click);
            // 
            // toolStrip1
            // 
            this.toolStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.toolStripButtonNewContact,
            this.toolStripButtonNewCertificate});
            this.toolStrip1.Location = new System.Drawing.Point(0, 24);
            this.toolStrip1.Name = "toolStrip1";
            this.toolStrip1.Size = new System.Drawing.Size(784, 39);
            this.toolStrip1.TabIndex = 11;
            this.toolStrip1.Text = "toolStrip1";
            // 
            // toolStripButtonNewContact
            // 
            this.toolStripButtonNewContact.Image = ((System.Drawing.Image)(resources.GetObject("toolStripButtonNewContact.Image")));
            this.toolStripButtonNewContact.ImageScaling = System.Windows.Forms.ToolStripItemImageScaling.None;
            this.toolStripButtonNewContact.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.toolStripButtonNewContact.Name = "toolStripButtonNewContact";
            this.toolStripButtonNewContact.Size = new System.Drawing.Size(112, 36);
            this.toolStripButtonNewContact.Text = "New Contact";
            this.toolStripButtonNewContact.Click += new System.EventHandler(this.toolStripButtonNewContact_Click);
            // 
            // toolStripButtonNewCertificate
            // 
            this.toolStripButtonNewCertificate.Image = ((System.Drawing.Image)(resources.GetObject("toolStripButtonNewCertificate.Image")));
            this.toolStripButtonNewCertificate.ImageScaling = System.Windows.Forms.ToolStripItemImageScaling.None;
            this.toolStripButtonNewCertificate.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.toolStripButtonNewCertificate.Name = "toolStripButtonNewCertificate";
            this.toolStripButtonNewCertificate.Size = new System.Drawing.Size(124, 36);
            this.toolStripButtonNewCertificate.Text = "New Certificate";
            this.toolStripButtonNewCertificate.Click += new System.EventHandler(this.toolStripButtonNewCertificate_Click);
            // 
            // tabControl1
            // 
            this.tabControl1.Controls.Add(this.tabPageManagedSites);
            this.tabControl1.Controls.Add(this.tabPageVault);
            this.tabControl1.Controls.Add(this.tabPageLog);
            this.tabControl1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tabControl1.Location = new System.Drawing.Point(0, 63);
            this.tabControl1.Name = "tabControl1";
            this.tabControl1.SelectedIndex = 0;
            this.tabControl1.Size = new System.Drawing.Size(784, 498);
            this.tabControl1.TabIndex = 12;
            // 
            // tabPageManagedSites
            // 
            this.tabPageManagedSites.Controls.Add(this.managedSites1);
            this.tabPageManagedSites.Location = new System.Drawing.Point(4, 22);
            this.tabPageManagedSites.Name = "tabPageManagedSites";
            this.tabPageManagedSites.Padding = new System.Windows.Forms.Padding(3);
            this.tabPageManagedSites.Size = new System.Drawing.Size(776, 472);
            this.tabPageManagedSites.TabIndex = 2;
            this.tabPageManagedSites.Text = "Managed Sites";
            this.tabPageManagedSites.UseVisualStyleBackColor = true;
            // 
            // managedSites1
            // 
            this.managedSites1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.managedSites1.Location = new System.Drawing.Point(3, 3);
            this.managedSites1.Name = "managedSites1";
            this.managedSites1.Size = new System.Drawing.Size(770, 466);
            this.managedSites1.TabIndex = 0;
            // 
            // tabPageVault
            // 
            this.tabPageVault.Controls.Add(this.vaultExplorer1);
            this.tabPageVault.Location = new System.Drawing.Point(4, 22);
            this.tabPageVault.Name = "tabPageVault";
            this.tabPageVault.Padding = new System.Windows.Forms.Padding(3);
            this.tabPageVault.Size = new System.Drawing.Size(776, 472);
            this.tabPageVault.TabIndex = 0;
            this.tabPageVault.Text = "Vault";
            this.tabPageVault.UseVisualStyleBackColor = true;
            // 
            // tabPageLog
            // 
            this.tabPageLog.Controls.Add(this.txtOutput);
            this.tabPageLog.Location = new System.Drawing.Point(4, 22);
            this.tabPageLog.Name = "tabPageLog";
            this.tabPageLog.Padding = new System.Windows.Forms.Padding(3);
            this.tabPageLog.Size = new System.Drawing.Size(776, 472);
            this.tabPageLog.TabIndex = 1;
            this.tabPageLog.Text = "Log";
            this.tabPageLog.UseVisualStyleBackColor = true;
            // 
            // txtOutput
            // 
            this.txtOutput.Dock = System.Windows.Forms.DockStyle.Fill;
            this.txtOutput.Location = new System.Drawing.Point(3, 3);
            this.txtOutput.Multiline = true;
            this.txtOutput.Name = "txtOutput";
            this.txtOutput.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.txtOutput.Size = new System.Drawing.Size(770, 466);
            this.txtOutput.TabIndex = 14;
            // 
            // vaultExplorer1
            // 
            this.vaultExplorer1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.vaultExplorer1.Location = new System.Drawing.Point(3, 3);
            this.vaultExplorer1.Name = "vaultExplorer1";
            this.vaultExplorer1.Size = new System.Drawing.Size(770, 466);
            this.vaultExplorer1.TabIndex = 0;
            // 
            // MainForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.SystemColors.Window;
            this.ClientSize = new System.Drawing.Size(784, 561);
            this.Controls.Add(this.tabControl1);
            this.Controls.Add(this.toolStrip1);
            this.Controls.Add(this.menuStrip1);
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.MainMenuStrip = this.menuStrip1;
            this.MinimumSize = new System.Drawing.Size(680, 300);
            this.Name = "MainForm";
            this.Text = "Certify";
            this.WindowState = System.Windows.Forms.FormWindowState.Maximized;
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.MainForm_FormClosing);
            this.Load += new System.EventHandler(this.MainForm_Load);
            this.Shown += new System.EventHandler(this.MainForm_Shown);
            this.menuStrip1.ResumeLayout(false);
            this.menuStrip1.PerformLayout();
            this.toolStrip1.ResumeLayout(false);
            this.toolStrip1.PerformLayout();
            this.tabControl1.ResumeLayout(false);
            this.tabPageManagedSites.ResumeLayout(false);
            this.tabPageVault.ResumeLayout(false);
            this.tabPageLog.ResumeLayout(false);
            this.tabPageLog.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.MenuStrip menuStrip1;
        private System.Windows.Forms.ToolStripMenuItem toolStripMenuItem1;
        private System.Windows.Forms.ToolStripMenuItem fileToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem helpToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem aboutToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem reloadVaultToolStripMenuItem;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator1;
        private System.Windows.Forms.ToolStripMenuItem checkForUpdatesToolStripMenuItem;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator2;
        private System.Windows.Forms.ToolStripMenuItem newToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem contactRegistrationToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem domainCertificateToolStripMenuItem;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator3;
        private System.Windows.Forms.ToolStrip toolStrip1;
        private System.Windows.Forms.ToolStripButton toolStripButtonNewContact;
        private System.Windows.Forms.ToolStripButton toolStripButtonNewCertificate;
        private System.Windows.Forms.ToolStripMenuItem websiteToolStripMenuItem;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator4;
        private System.Windows.Forms.TabControl tabControl1;
        private System.Windows.Forms.TabPage tabPageVault;
        private System.Windows.Forms.TabPage tabPageLog;
        private System.Windows.Forms.TextBox txtOutput;
        private System.Windows.Forms.ToolStripMenuItem optionsToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem settingsToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem cleanupVaultToolStripMenuItem;
        private System.Windows.Forms.TabPage tabPageManagedSites;
        private Forms.Controls.ManagedSites managedSites1;
        private Forms.Controls.VaultExplorer vaultExplorer1;
    }
}

