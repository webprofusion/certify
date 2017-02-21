namespace Certify.Forms.Controls
{
    partial class CertRequestSettingsIIS
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

        #region Component Designer generated code

        /// <summary> 
        /// Required method for Designer support - do not modify 
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.groupBox1 = new System.Windows.Forms.GroupBox();
            this.chkSkipConfigCheck = new System.Windows.Forms.CheckBox();
            this.progressBar1 = new System.Windows.Forms.ProgressBar();
            this.chkAutoBindings = new System.Windows.Forms.CheckBox();
            this.lblDomain = new System.Windows.Forms.Label();
            this.label3 = new System.Windows.Forms.Label();
            this.btnCancel = new System.Windows.Forms.Button();
            this.lblWebsiteRoot = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.lstSites = new System.Windows.Forms.ComboBox();
            this.label1 = new System.Windows.Forms.Label();
            this.btnRequestCertificate = new System.Windows.Forms.Button();
            this.chkIncludeInAutoRenew = new System.Windows.Forms.CheckBox();
            this.groupBox1.SuspendLayout();
            this.SuspendLayout();
            // 
            // groupBox1
            // 
            this.groupBox1.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.groupBox1.Controls.Add(this.chkIncludeInAutoRenew);
            this.groupBox1.Controls.Add(this.chkSkipConfigCheck);
            this.groupBox1.Controls.Add(this.progressBar1);
            this.groupBox1.Controls.Add(this.chkAutoBindings);
            this.groupBox1.Controls.Add(this.lblDomain);
            this.groupBox1.Controls.Add(this.label3);
            this.groupBox1.Controls.Add(this.btnCancel);
            this.groupBox1.Controls.Add(this.lblWebsiteRoot);
            this.groupBox1.Controls.Add(this.label2);
            this.groupBox1.Controls.Add(this.lstSites);
            this.groupBox1.Controls.Add(this.label1);
            this.groupBox1.Controls.Add(this.btnRequestCertificate);
            this.groupBox1.Location = new System.Drawing.Point(3, 3);
            this.groupBox1.Name = "groupBox1";
            this.groupBox1.Size = new System.Drawing.Size(448, 247);
            this.groupBox1.TabIndex = 11;
            this.groupBox1.TabStop = false;
            this.groupBox1.Text = "IIS Certificate Request";
            // 
            // chkSkipConfigCheck
            // 
            this.chkSkipConfigCheck.AutoSize = true;
            this.chkSkipConfigCheck.Location = new System.Drawing.Point(15, 94);
            this.chkSkipConfigCheck.Name = "chkSkipConfigCheck";
            this.chkSkipConfigCheck.Size = new System.Drawing.Size(223, 17);
            this.chkSkipConfigCheck.TabIndex = 23;
            this.chkSkipConfigCheck.Text = "Skip challenge response file config check";
            this.chkSkipConfigCheck.UseVisualStyleBackColor = true;
            // 
            // progressBar1
            // 
            this.progressBar1.Location = new System.Drawing.Point(15, 204);
            this.progressBar1.Name = "progressBar1";
            this.progressBar1.Size = new System.Drawing.Size(152, 23);
            this.progressBar1.Style = System.Windows.Forms.ProgressBarStyle.Marquee;
            this.progressBar1.TabIndex = 22;
            // 
            // chkAutoBindings
            // 
            this.chkAutoBindings.AutoSize = true;
            this.chkAutoBindings.Checked = true;
            this.chkAutoBindings.CheckState = System.Windows.Forms.CheckState.Checked;
            this.chkAutoBindings.Location = new System.Drawing.Point(15, 71);
            this.chkAutoBindings.Name = "chkAutoBindings";
            this.chkAutoBindings.Size = new System.Drawing.Size(229, 17);
            this.chkAutoBindings.TabIndex = 21;
            this.chkAutoBindings.Text = "Auto create/update IIS bindings (uses SNI)";
            this.chkAutoBindings.UseVisualStyleBackColor = true;
            // 
            // lblDomain
            // 
            this.lblDomain.AutoSize = true;
            this.lblDomain.Location = new System.Drawing.Point(90, 179);
            this.lblDomain.Name = "lblDomain";
            this.lblDomain.Size = new System.Drawing.Size(77, 13);
            this.lblDomain.TabIndex = 20;
            this.lblDomain.Text = "(Not Specified)";
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Location = new System.Drawing.Point(12, 179);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(43, 13);
            this.label3.TabIndex = 19;
            this.label3.Text = "Domain";
            // 
            // btnCancel
            // 
            this.btnCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.btnCancel.Location = new System.Drawing.Point(199, 204);
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Size = new System.Drawing.Size(82, 23);
            this.btnCancel.TabIndex = 18;
            this.btnCancel.Text = "Cancel";
            this.btnCancel.UseVisualStyleBackColor = true;
            this.btnCancel.Click += new System.EventHandler(this.btnCancel_Click);
            // 
            // lblWebsiteRoot
            // 
            this.lblWebsiteRoot.AutoSize = true;
            this.lblWebsiteRoot.Location = new System.Drawing.Point(90, 157);
            this.lblWebsiteRoot.Name = "lblWebsiteRoot";
            this.lblWebsiteRoot.Size = new System.Drawing.Size(77, 13);
            this.lblWebsiteRoot.TabIndex = 17;
            this.lblWebsiteRoot.Text = "(Not Specified)";
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(12, 157);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(72, 13);
            this.label2.TabIndex = 16;
            this.label2.Text = "Website Root";
            // 
            // lstSites
            // 
            this.lstSites.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.lstSites.FormattingEnabled = true;
            this.lstSites.Location = new System.Drawing.Point(15, 41);
            this.lstSites.Name = "lstSites";
            this.lstSites.Size = new System.Drawing.Size(389, 21);
            this.lstSites.TabIndex = 15;
            this.lstSites.SelectedIndexChanged += new System.EventHandler(this.lstSites_SelectedIndexChanged);
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(12, 25);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(46, 13);
            this.label1.TabIndex = 14;
            this.label1.Text = "Website";
            // 
            // btnRequestCertificate
            // 
            this.btnRequestCertificate.Location = new System.Drawing.Point(302, 204);
            this.btnRequestCertificate.Name = "btnRequestCertificate";
            this.btnRequestCertificate.Size = new System.Drawing.Size(128, 23);
            this.btnRequestCertificate.TabIndex = 13;
            this.btnRequestCertificate.Text = "Request Certificate";
            this.btnRequestCertificate.UseVisualStyleBackColor = true;
            this.btnRequestCertificate.Click += new System.EventHandler(this.btnRequestCertificate_Click);
            // 
            // chkIncludeInAutoRenew
            // 
            this.chkIncludeInAutoRenew.AutoSize = true;
            this.chkIncludeInAutoRenew.Checked = true;
            this.chkIncludeInAutoRenew.CheckState = System.Windows.Forms.CheckState.Checked;
            this.chkIncludeInAutoRenew.Location = new System.Drawing.Point(15, 117);
            this.chkIncludeInAutoRenew.Name = "chkIncludeInAutoRenew";
            this.chkIncludeInAutoRenew.Size = new System.Drawing.Size(129, 17);
            this.chkIncludeInAutoRenew.TabIndex = 24;
            this.chkIncludeInAutoRenew.Text = "Enable Auto Renewal";
            this.chkIncludeInAutoRenew.UseVisualStyleBackColor = true;
            // 
            // CertRequestSettingsIIS
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.groupBox1);
            this.Name = "CertRequestSettingsIIS";
            this.Size = new System.Drawing.Size(465, 264);
            this.Load += new System.EventHandler(this.CertRequestSettingsIIS_Load);
            this.groupBox1.ResumeLayout(false);
            this.groupBox1.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.GroupBox groupBox1;
        private System.Windows.Forms.Button btnCancel;
        private System.Windows.Forms.Label lblWebsiteRoot;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.ComboBox lstSites;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Button btnRequestCertificate;
        private System.Windows.Forms.Label lblDomain;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.CheckBox chkAutoBindings;
        private System.Windows.Forms.ProgressBar progressBar1;
        private System.Windows.Forms.CheckBox chkSkipConfigCheck;
        private System.Windows.Forms.CheckBox chkIncludeInAutoRenew;
    }
}
