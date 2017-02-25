namespace Certify.Forms.Controls
{
    partial class VaultExplorer
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(VaultExplorer));
            this.splitContainer1 = new System.Windows.Forms.SplitContainer();
            this.treeView1 = new System.Windows.Forms.TreeView();
            this.groupBoxVaultInfo = new System.Windows.Forms.GroupBox();
            this.lblAPIBaseURI = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.lblVaultLocation = new System.Windows.Forms.Label();
            this.label1 = new System.Windows.Forms.Label();
            this.panelItemInfo = new System.Windows.Forms.Panel();
            this.lblGettingStarted = new System.Windows.Forms.Label();
            this.imageList1 = new System.Windows.Forms.ImageList(this.components);
            this.treeViewContextMenu = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.deleteToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.label3 = new System.Windows.Forms.Label();
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer1)).BeginInit();
            this.splitContainer1.Panel1.SuspendLayout();
            this.splitContainer1.Panel2.SuspendLayout();
            this.splitContainer1.SuspendLayout();
            this.groupBoxVaultInfo.SuspendLayout();
            this.panelItemInfo.SuspendLayout();
            this.treeViewContextMenu.SuspendLayout();
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
            this.splitContainer1.Panel1.Controls.Add(this.treeView1);
            // 
            // splitContainer1.Panel2
            // 
            this.splitContainer1.Panel2.Controls.Add(this.groupBoxVaultInfo);
            this.splitContainer1.Panel2.Controls.Add(this.panelItemInfo);
            this.splitContainer1.Size = new System.Drawing.Size(707, 467);
            this.splitContainer1.SplitterDistance = 208;
            this.splitContainer1.TabIndex = 16;
            // 
            // treeView1
            // 
            this.treeView1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.treeView1.Location = new System.Drawing.Point(0, 0);
            this.treeView1.Name = "treeView1";
            this.treeView1.Size = new System.Drawing.Size(208, 467);
            this.treeView1.TabIndex = 13;
            this.treeView1.AfterSelect += new System.Windows.Forms.TreeViewEventHandler(this.treeView1_AfterSelect);
            this.treeView1.MouseClick += new System.Windows.Forms.MouseEventHandler(this.treeView1_MouseClick);
            // 
            // groupBoxVaultInfo
            // 
            this.groupBoxVaultInfo.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.groupBoxVaultInfo.Controls.Add(this.label3);
            this.groupBoxVaultInfo.Controls.Add(this.lblAPIBaseURI);
            this.groupBoxVaultInfo.Controls.Add(this.label2);
            this.groupBoxVaultInfo.Controls.Add(this.lblVaultLocation);
            this.groupBoxVaultInfo.Controls.Add(this.label1);
            this.groupBoxVaultInfo.Location = new System.Drawing.Point(3, 3);
            this.groupBoxVaultInfo.Name = "groupBoxVaultInfo";
            this.groupBoxVaultInfo.Size = new System.Drawing.Size(491, 105);
            this.groupBoxVaultInfo.TabIndex = 16;
            this.groupBoxVaultInfo.TabStop = false;
            this.groupBoxVaultInfo.Text = "Vault Info";
            this.groupBoxVaultInfo.Enter += new System.EventHandler(this.groupBoxVaultInfo_Enter);
            // 
            // lblAPIBaseURI
            // 
            this.lblAPIBaseURI.AutoSize = true;
            this.lblAPIBaseURI.Location = new System.Drawing.Point(99, 69);
            this.lblAPIBaseURI.Name = "lblAPIBaseURI";
            this.lblAPIBaseURI.Size = new System.Drawing.Size(37, 13);
            this.lblAPIBaseURI.TabIndex = 3;
            this.lblAPIBaseURI.Text = "(none)";
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(15, 69);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(73, 13);
            this.label2.TabIndex = 2;
            this.label2.Text = "API Base URI";
            // 
            // lblVaultLocation
            // 
            this.lblVaultLocation.AutoSize = true;
            this.lblVaultLocation.Location = new System.Drawing.Point(99, 47);
            this.lblVaultLocation.Name = "lblVaultLocation";
            this.lblVaultLocation.Size = new System.Drawing.Size(37, 13);
            this.lblVaultLocation.TabIndex = 1;
            this.lblVaultLocation.Text = "(none)";
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(15, 47);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(78, 13);
            this.label1.TabIndex = 0;
            this.label1.Text = "Vault Location:";
            // 
            // panelItemInfo
            // 
            this.panelItemInfo.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.panelItemInfo.Controls.Add(this.lblGettingStarted);
            this.panelItemInfo.Location = new System.Drawing.Point(3, 114);
            this.panelItemInfo.Name = "panelItemInfo";
            this.panelItemInfo.Size = new System.Drawing.Size(494, 348);
            this.panelItemInfo.TabIndex = 15;
            // 
            // lblGettingStarted
            // 
            this.lblGettingStarted.AutoSize = true;
            this.lblGettingStarted.Location = new System.Drawing.Point(15, 22);
            this.lblGettingStarted.Name = "lblGettingStarted";
            this.lblGettingStarted.Size = new System.Drawing.Size(311, 13);
            this.lblGettingStarted.TabIndex = 0;
            this.lblGettingStarted.Text = "Browse the Vault on the left to see current certificate information.";
            // 
            // imageList1
            // 
            this.imageList1.ImageStream = ((System.Windows.Forms.ImageListStreamer)(resources.GetObject("imageList1.ImageStream")));
            this.imageList1.TransparentColor = System.Drawing.Color.Transparent;
            this.imageList1.Images.SetKeyName(0, "fa-lock_16_0_303030_none.png");
            this.imageList1.Images.SetKeyName(1, "fa-globe_16_0_303030_none.png");
            this.imageList1.Images.SetKeyName(2, "fa-certificate_16_0_303030_none.png");
            this.imageList1.Images.SetKeyName(3, "fa-user_16_0_303030_none.png");
            // 
            // treeViewContextMenu
            // 
            this.treeViewContextMenu.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.deleteToolStripMenuItem});
            this.treeViewContextMenu.Name = "treeViewContextMenu";
            this.treeViewContextMenu.Size = new System.Drawing.Size(108, 26);
            // 
            // deleteToolStripMenuItem
            // 
            this.deleteToolStripMenuItem.Name = "deleteToolStripMenuItem";
            this.deleteToolStripMenuItem.Size = new System.Drawing.Size(107, 22);
            this.deleteToolStripMenuItem.Text = "Delete";
            this.deleteToolStripMenuItem.Click += new System.EventHandler(this.deleteToolStripMenuItem_Click);
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Location = new System.Drawing.Point(15, 25);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(459, 13);
            this.label3.TabIndex = 4;
            this.label3.Text = "The ACME Vault manages information used when communicating with the Let\'s Encrypt" +
    " service.";
            // 
            // VaultExplorer
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.splitContainer1);
            this.Name = "VaultExplorer";
            this.Size = new System.Drawing.Size(707, 467);
            this.Load += new System.EventHandler(this.VaultExplorer_Load);
            this.splitContainer1.Panel1.ResumeLayout(false);
            this.splitContainer1.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer1)).EndInit();
            this.splitContainer1.ResumeLayout(false);
            this.groupBoxVaultInfo.ResumeLayout(false);
            this.groupBoxVaultInfo.PerformLayout();
            this.panelItemInfo.ResumeLayout(false);
            this.panelItemInfo.PerformLayout();
            this.treeViewContextMenu.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.SplitContainer splitContainer1;
        private System.Windows.Forms.TreeView treeView1;
        private System.Windows.Forms.GroupBox groupBoxVaultInfo;
        private System.Windows.Forms.Label lblAPIBaseURI;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Label lblVaultLocation;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Panel panelItemInfo;
        private System.Windows.Forms.Label lblGettingStarted;
        private System.Windows.Forms.ImageList imageList1;
        private System.Windows.Forms.ContextMenuStrip treeViewContextMenu;
        private System.Windows.Forms.ToolStripMenuItem deleteToolStripMenuItem;
        private System.Windows.Forms.Label label3;
    }
}
