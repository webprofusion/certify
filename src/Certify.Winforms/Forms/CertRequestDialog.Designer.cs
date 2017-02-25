namespace Certify.Forms
{
    partial class CertRequestDialog
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(CertRequestDialog));
            this.lstRequestType = new System.Windows.Forms.ComboBox();
            this.label1 = new System.Windows.Forms.Label();
            this.panelCertControl = new System.Windows.Forms.Panel();
            this.SuspendLayout();
            // 
            // lstRequestType
            // 
            this.lstRequestType.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.lstRequestType.FormattingEnabled = true;
            this.lstRequestType.Items.AddRange(new object[] {
            "IIS Website",
            "Any Web Server Type"});
            this.lstRequestType.Location = new System.Drawing.Point(119, 15);
            this.lstRequestType.Name = "lstRequestType";
            this.lstRequestType.Size = new System.Drawing.Size(249, 21);
            this.lstRequestType.TabIndex = 1;
            this.lstRequestType.SelectedIndexChanged += new System.EventHandler(this.lstRequestType_SelectedIndexChanged);
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(24, 15);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(74, 13);
            this.label1.TabIndex = 2;
            this.label1.Text = "Request Type";
            // 
            // panelCertControl
            // 
            this.panelCertControl.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.panelCertControl.Location = new System.Drawing.Point(27, 45);
            this.panelCertControl.Name = "panelCertControl";
            this.panelCertControl.Size = new System.Drawing.Size(607, 367);
            this.panelCertControl.TabIndex = 3;
            // 
            // CertRequestDialog
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(645, 425);
            this.Controls.Add(this.panelCertControl);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.lstRequestType);
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Name = "CertRequestDialog";
            this.Text = "Perform Certificate Request";
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion
        private System.Windows.Forms.ComboBox lstRequestType;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Panel panelCertControl;
    }
}