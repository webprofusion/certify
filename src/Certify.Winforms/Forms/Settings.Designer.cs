namespace Certify.Forms
{
    partial class Settings
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(Settings));
            this.btnSave = new System.Windows.Forms.Button();
            this.btnCancel = new System.Windows.Forms.Button();
            this.chkAutoCheckForUpdates = new System.Windows.Forms.CheckBox();
            this.btnLockdown = new System.Windows.Forms.Button();
            this.label1 = new System.Windows.Forms.Label();
            this.chkAnalytics = new System.Windows.Forms.CheckBox();
            this.chkShowOnlyStartedWebsites = new System.Windows.Forms.CheckBox();
            this.SuspendLayout();
            // 
            // btnSave
            // 
            this.btnSave.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.btnSave.DialogResult = System.Windows.Forms.DialogResult.OK;
            this.btnSave.Location = new System.Drawing.Point(296, 146);
            this.btnSave.Name = "btnSave";
            this.btnSave.Size = new System.Drawing.Size(75, 23);
            this.btnSave.TabIndex = 0;
            this.btnSave.Text = "Save";
            this.btnSave.UseVisualStyleBackColor = true;
            // 
            // btnCancel
            // 
            this.btnCancel.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.btnCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.btnCancel.Location = new System.Drawing.Point(211, 146);
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Size = new System.Drawing.Size(75, 23);
            this.btnCancel.TabIndex = 1;
            this.btnCancel.Text = "Cancel";
            this.btnCancel.UseVisualStyleBackColor = true;
            // 
            // chkAutoCheckForUpdates
            // 
            this.chkAutoCheckForUpdates.AutoSize = true;
            this.chkAutoCheckForUpdates.Location = new System.Drawing.Point(38, 12);
            this.chkAutoCheckForUpdates.Name = "chkAutoCheckForUpdates";
            this.chkAutoCheckForUpdates.Size = new System.Drawing.Size(183, 17);
            this.chkAutoCheckForUpdates.TabIndex = 4;
            this.chkAutoCheckForUpdates.Text = "Automatically Check For Updates";
            this.chkAutoCheckForUpdates.UseVisualStyleBackColor = true;
            // 
            // btnLockdown
            // 
            this.btnLockdown.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.btnLockdown.Location = new System.Drawing.Point(296, 90);
            this.btnLockdown.Name = "btnLockdown";
            this.btnLockdown.Size = new System.Drawing.Size(75, 23);
            this.btnLockdown.TabIndex = 5;
            this.btnLockdown.Text = "Lockdown";
            this.btnLockdown.UseVisualStyleBackColor = true;
            this.btnLockdown.Click += new System.EventHandler(this.btnLockdown_Click);
            // 
            // label1
            // 
            this.label1.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(41, 95);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(245, 13);
            this.label1.TabIndex = 6;
            this.label1.Text = "Automatically disable insecure SSL/TLS protocols:";
            this.label1.Click += new System.EventHandler(this.label1_Click);
            // 
            // chkAnalytics
            // 
            this.chkAnalytics.AutoSize = true;
            this.chkAnalytics.Location = new System.Drawing.Point(38, 35);
            this.chkAnalytics.Name = "chkAnalytics";
            this.chkAnalytics.Size = new System.Drawing.Size(260, 17);
            this.chkAnalytics.TabIndex = 7;
            this.chkAnalytics.Text = "Send Usage Data and Crash Reports to Publisher";
            this.chkAnalytics.UseVisualStyleBackColor = true;
            // 
            // chkShowOnlyStartedWebsites
            // 
            this.chkShowOnlyStartedWebsites.AutoSize = true;
            this.chkShowOnlyStartedWebsites.Location = new System.Drawing.Point(38, 58);
            this.chkShowOnlyStartedWebsites.Name = "chkShowOnlyStartedWebsites";
            this.chkShowOnlyStartedWebsites.Size = new System.Drawing.Size(193, 17);
            this.chkShowOnlyStartedWebsites.TabIndex = 8;
            this.chkShowOnlyStartedWebsites.Text = "Show Only Webssites Started in IIS";
            this.chkShowOnlyStartedWebsites.UseVisualStyleBackColor = true;
            // 
            // Settings
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(383, 181);
            this.Controls.Add(this.chkShowOnlyStartedWebsites);
            this.Controls.Add(this.chkAnalytics);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.btnLockdown);
            this.Controls.Add(this.chkAutoCheckForUpdates);
            this.Controls.Add(this.btnCancel);
            this.Controls.Add(this.btnSave);
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Name = "Settings";
            this.Text = "Settings";
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Button btnSave;
        private System.Windows.Forms.Button btnCancel;
        private System.Windows.Forms.CheckBox chkAutoCheckForUpdates;
        private System.Windows.Forms.Button btnLockdown;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.CheckBox chkAnalytics;
        private System.Windows.Forms.CheckBox chkShowOnlyStartedWebsites;
    }
}