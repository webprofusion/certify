namespace Certify.Forms.Controls
{
    partial class ManagedSites
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
            this.components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(ManagedSites));
            System.Windows.Forms.ListViewGroup listViewGroup3 = new System.Windows.Forms.ListViewGroup("Auto Renewed", System.Windows.Forms.HorizontalAlignment.Left);
            System.Windows.Forms.ListViewGroup listViewGroup4 = new System.Windows.Forms.ListViewGroup("Manual Renewals", System.Windows.Forms.HorizontalAlignment.Left);
            System.Windows.Forms.ListViewItem listViewItem2 = new System.Windows.Forms.ListViewItem("No Managed Sites", 0);
            this.splitContainer1 = new System.Windows.Forms.SplitContainer();
            this.btnRenewAll = new System.Windows.Forms.Button();
            this.imageList1 = new System.Windows.Forms.ImageList(this.components);
            this.listView1 = new System.Windows.Forms.ListView();
            this.panel1 = new System.Windows.Forms.Panel();
            this.checkedListBox1 = new System.Windows.Forms.CheckedListBox();
            this.lblRenewalContact = new System.Windows.Forms.Label();
            this.label5 = new System.Windows.Forms.Label();
            this.label4 = new System.Windows.Forms.Label();
            this.lblDateLastRenewed = new System.Windows.Forms.Label();
            this.label3 = new System.Windows.Forms.Label();
            this.lblAutoRenew = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.lblPrimarySubjectDomain = new System.Windows.Forms.Label();
            this.label1 = new System.Windows.Forms.Label();
            this.lblInfo = new System.Windows.Forms.Label();
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer1)).BeginInit();
            this.splitContainer1.Panel1.SuspendLayout();
            this.splitContainer1.Panel2.SuspendLayout();
            this.splitContainer1.SuspendLayout();
            this.panel1.SuspendLayout();
            this.SuspendLayout();
            // 
            // splitContainer1
            // 
            this.splitContainer1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.splitContainer1.Location = new System.Drawing.Point(0, 0);
            this.splitContainer1.Name = "splitContainer1";
            // 
            // splitContainer1.Panel1
            // 
            this.splitContainer1.Panel1.Controls.Add(this.btnRenewAll);
            this.splitContainer1.Panel1.Controls.Add(this.listView1);
            // 
            // splitContainer1.Panel2
            // 
            this.splitContainer1.Panel2.Controls.Add(this.panel1);
            this.splitContainer1.Panel2.Controls.Add(this.lblInfo);
            this.splitContainer1.Size = new System.Drawing.Size(806, 363);
            this.splitContainer1.SplitterDistance = 268;
            this.splitContainer1.TabIndex = 0;
            // 
            // btnRenewAll
            // 
            this.btnRenewAll.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.btnRenewAll.ImageIndex = 0;
            this.btnRenewAll.ImageList = this.imageList1;
            this.btnRenewAll.Location = new System.Drawing.Point(3, 0);
            this.btnRenewAll.Name = "btnRenewAll";
            this.btnRenewAll.Size = new System.Drawing.Size(98, 33);
            this.btnRenewAll.TabIndex = 2;
            this.btnRenewAll.Text = "Renew All";
            this.btnRenewAll.UseVisualStyleBackColor = true;
            this.btnRenewAll.Click += new System.EventHandler(this.btnRenewAll_Click);
            // 
            // imageList1
            // 
            this.imageList1.ImageStream = ((System.Windows.Forms.ImageListStreamer)(resources.GetObject("imageList1.ImageStream")));
            this.imageList1.TransparentColor = System.Drawing.Color.Transparent;
            this.imageList1.Images.SetKeyName(0, "font-awesome_4-7-0_globe_24_0_2c3e50_none.png");
            // 
            // listView1
            // 
            this.listView1.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            listViewGroup3.Header = "Auto Renewed";
            listViewGroup3.Name = "listViewGroupAutoRenew";
            listViewGroup4.Header = "Manual Renewals";
            listViewGroup4.Name = "listViewGroupManualRenew";
            this.listView1.Groups.AddRange(new System.Windows.Forms.ListViewGroup[] {
            listViewGroup3,
            listViewGroup4});
            this.listView1.Items.AddRange(new System.Windows.Forms.ListViewItem[] {
            listViewItem2});
            this.listView1.LargeImageList = this.imageList1;
            this.listView1.Location = new System.Drawing.Point(0, 34);
            this.listView1.Name = "listView1";
            this.listView1.Size = new System.Drawing.Size(268, 329);
            this.listView1.SmallImageList = this.imageList1;
            this.listView1.TabIndex = 1;
            this.listView1.UseCompatibleStateImageBehavior = false;
            this.listView1.View = System.Windows.Forms.View.Tile;
            this.listView1.SelectedIndexChanged += new System.EventHandler(this.listView1_SelectedIndexChanged);
            // 
            // panel1
            // 
            this.panel1.Controls.Add(this.checkedListBox1);
            this.panel1.Controls.Add(this.lblRenewalContact);
            this.panel1.Controls.Add(this.label5);
            this.panel1.Controls.Add(this.label4);
            this.panel1.Controls.Add(this.lblDateLastRenewed);
            this.panel1.Controls.Add(this.label3);
            this.panel1.Controls.Add(this.lblAutoRenew);
            this.panel1.Controls.Add(this.label2);
            this.panel1.Controls.Add(this.lblPrimarySubjectDomain);
            this.panel1.Controls.Add(this.label1);
            this.panel1.Location = new System.Drawing.Point(16, 34);
            this.panel1.Name = "panel1";
            this.panel1.Size = new System.Drawing.Size(479, 291);
            this.panel1.TabIndex = 1;
            this.panel1.Visible = false;
            // 
            // checkedListBox1
            // 
            this.checkedListBox1.FormattingEnabled = true;
            this.checkedListBox1.Location = new System.Drawing.Point(17, 130);
            this.checkedListBox1.Name = "checkedListBox1";
            this.checkedListBox1.Size = new System.Drawing.Size(437, 94);
            this.checkedListBox1.TabIndex = 9;
            // 
            // lblRenewalContact
            // 
            this.lblRenewalContact.AutoSize = true;
            this.lblRenewalContact.Location = new System.Drawing.Point(191, 87);
            this.lblRenewalContact.Name = "lblRenewalContact";
            this.lblRenewalContact.Size = new System.Drawing.Size(171, 13);
            this.lblRenewalContact.TabIndex = 8;
            this.lblRenewalContact.Text = "Contact Email for Renewal Failures";
            // 
            // label5
            // 
            this.label5.AutoSize = true;
            this.label5.Location = new System.Drawing.Point(14, 87);
            this.label5.Name = "label5";
            this.label5.Size = new System.Drawing.Size(171, 13);
            this.label5.TabIndex = 7;
            this.label5.Text = "Contact Email for Renewal Failures";
            // 
            // label4
            // 
            this.label4.AutoSize = true;
            this.label4.Location = new System.Drawing.Point(14, 113);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(132, 13);
            this.label4.TabIndex = 6;
            this.label4.Text = "Subject Alternative Names";
            // 
            // lblDateLastRenewed
            // 
            this.lblDateLastRenewed.AutoSize = true;
            this.lblDateLastRenewed.Location = new System.Drawing.Point(191, 63);
            this.lblDateLastRenewed.Name = "lblDateLastRenewed";
            this.lblDateLastRenewed.Size = new System.Drawing.Size(30, 13);
            this.lblDateLastRenewed.TabIndex = 5;
            this.lblDateLastRenewed.Text = "Date";
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Location = new System.Drawing.Point(14, 63);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(102, 13);
            this.label3.TabIndex = 4;
            this.label3.Text = "Date Last Renewed";
            // 
            // lblAutoRenew
            // 
            this.lblAutoRenew.AutoSize = true;
            this.lblAutoRenew.Location = new System.Drawing.Point(191, 41);
            this.lblAutoRenew.Name = "lblAutoRenew";
            this.lblAutoRenew.Size = new System.Drawing.Size(66, 13);
            this.lblAutoRenew.TabIndex = 3;
            this.lblAutoRenew.Text = "Auto Renew";
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(14, 41);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(66, 13);
            this.label2.TabIndex = 2;
            this.label2.Text = "Auto Renew";
            // 
            // lblPrimarySubjectDomain
            // 
            this.lblPrimarySubjectDomain.AutoSize = true;
            this.lblPrimarySubjectDomain.Location = new System.Drawing.Point(191, 16);
            this.lblPrimarySubjectDomain.Name = "lblPrimarySubjectDomain";
            this.lblPrimarySubjectDomain.Size = new System.Drawing.Size(125, 13);
            this.lblPrimarySubjectDomain.TabIndex = 1;
            this.lblPrimarySubjectDomain.Text = "Primary Subject (Domain)";
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(14, 16);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(125, 13);
            this.label1.TabIndex = 0;
            this.label1.Text = "Primary Subject (Domain)";
            // 
            // lblInfo
            // 
            this.lblInfo.AutoSize = true;
            this.lblInfo.Location = new System.Drawing.Point(13, 18);
            this.lblInfo.Name = "lblInfo";
            this.lblInfo.Size = new System.Drawing.Size(288, 13);
            this.lblInfo.TabIndex = 0;
            this.lblInfo.Text = "Select a Managed Site or create a New Certificate to begin.";
            // 
            // ManagedSites
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.splitContainer1);
            this.Name = "ManagedSites";
            this.Size = new System.Drawing.Size(806, 363);
            this.Load += new System.EventHandler(this.ManagedSites_Load);
            this.splitContainer1.Panel1.ResumeLayout(false);
            this.splitContainer1.Panel2.ResumeLayout(false);
            this.splitContainer1.Panel2.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer1)).EndInit();
            this.splitContainer1.ResumeLayout(false);
            this.panel1.ResumeLayout(false);
            this.panel1.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.SplitContainer splitContainer1;
        private System.Windows.Forms.Label lblInfo;
        private System.Windows.Forms.ListView listView1;
        private System.Windows.Forms.ImageList imageList1;
        private System.Windows.Forms.Panel panel1;
        private System.Windows.Forms.Label lblPrimarySubjectDomain;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Label lblRenewalContact;
        private System.Windows.Forms.Label label5;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.Label lblDateLastRenewed;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.Label lblAutoRenew;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.CheckedListBox checkedListBox1;
        private System.Windows.Forms.Button btnRenewAll;
    }
}
