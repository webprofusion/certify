namespace Certify.Forms
{
    partial class ContactRegistration
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(ContactRegistration));
            this.lblContact = new System.Windows.Forms.Label();
            this.btnCreateContact = new System.Windows.Forms.Button();
            this.btnCancel = new System.Windows.Forms.Button();
            this.txtContacts = new System.Windows.Forms.TextBox();
            this.label1 = new System.Windows.Forms.Label();
            this.chkAgreeTandCs = new System.Windows.Forms.CheckBox();
            this.label2 = new System.Windows.Forms.Label();
            this.SuspendLayout();
            // 
            // lblContact
            // 
            this.lblContact.AutoSize = true;
            this.lblContact.Location = new System.Drawing.Point(21, 18);
            this.lblContact.Name = "lblContact";
            this.lblContact.Size = new System.Drawing.Size(120, 13);
            this.lblContact.TabIndex = 0;
            this.lblContact.Text = "Contact (email address):";
            // 
            // btnCreateContact
            // 
            this.btnCreateContact.DialogResult = System.Windows.Forms.DialogResult.OK;
            this.btnCreateContact.Location = new System.Drawing.Point(276, 137);
            this.btnCreateContact.Name = "btnCreateContact";
            this.btnCreateContact.Size = new System.Drawing.Size(117, 23);
            this.btnCreateContact.TabIndex = 1;
            this.btnCreateContact.Text = "Register Contact";
            this.btnCreateContact.UseVisualStyleBackColor = true;
            this.btnCreateContact.Click += new System.EventHandler(this.btnCreateContact_Click);
            // 
            // btnCancel
            // 
            this.btnCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.btnCancel.Location = new System.Drawing.Point(195, 137);
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Size = new System.Drawing.Size(75, 23);
            this.btnCancel.TabIndex = 2;
            this.btnCancel.Text = "Cancel";
            this.btnCancel.UseVisualStyleBackColor = true;
            // 
            // txtContacts
            // 
            this.txtContacts.Location = new System.Drawing.Point(148, 18);
            this.txtContacts.Name = "txtContacts";
            this.txtContacts.Size = new System.Drawing.Size(245, 20);
            this.txtContacts.TabIndex = 3;
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(21, 63);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(326, 13);
            this.label1.TabIndex = 4;
            this.label1.Text = "Do you agree to the LetsEncrypt.org service Terms and Conditions?";
            this.label1.Click += new System.EventHandler(this.label1_Click);
            // 
            // chkAgreeTandCs
            // 
            this.chkAgreeTandCs.AutoSize = true;
            this.chkAgreeTandCs.Location = new System.Drawing.Point(24, 110);
            this.chkAgreeTandCs.Name = "chkAgreeTandCs";
            this.chkAgreeTandCs.Size = new System.Drawing.Size(84, 17);
            this.chkAgreeTandCs.TabIndex = 5;
            this.chkAgreeTandCs.Text = "Yes, I Agree";
            this.chkAgreeTandCs.UseVisualStyleBackColor = true;
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(21, 85);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(263, 13);
            this.label2.TabIndex = 6;
            this.label2.Text = "Refer to their website for full details before proceeding.";
            // 
            // ContactRegistration
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(405, 172);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.chkAgreeTandCs);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.txtContacts);
            this.Controls.Add(this.btnCancel);
            this.Controls.Add(this.btnCreateContact);
            this.Controls.Add(this.lblContact);
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Name = "ContactRegistration";
            this.Text = "Contact Registration";
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label lblContact;
        private System.Windows.Forms.Button btnCreateContact;
        private System.Windows.Forms.Button btnCancel;
        private System.Windows.Forms.TextBox txtContacts;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.CheckBox chkAgreeTandCs;
        private System.Windows.Forms.Label label2;
    }
}