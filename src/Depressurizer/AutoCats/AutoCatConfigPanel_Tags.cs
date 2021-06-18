﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Windows.Forms;
using Depressurizer.Core.Interfaces;
using Depressurizer.Properties;

namespace Depressurizer.AutoCats
{
    public partial class AutoCatConfigPanel_Tags : AutoCatConfigPanel
    {
        #region Fields

        private readonly IGameList ownedGames;

        private bool loaded;

        // used to remove unchecked items from the Tags checkedlistbox.
        private Thread workerThread;

        #endregion

        #region Constructors and Destructors

        public AutoCatConfigPanel_Tags(IGameList ownedGames)
        {
            this.ownedGames = ownedGames;

            InitializeComponent();

            ttHelp.Ext_SetToolTip(helpPrefix, GlobalStrings.DlgAutoCat_Help_Prefix);
            ttHelp.Ext_SetToolTip(list_helpMinScore, GlobalStrings.DlgAutoCat_Help_ListMinScore);
            ttHelp.Ext_SetToolTip(list_helpOwnedOnly, GlobalStrings.DlgAutoCat_Help_ListOwnedOnly);
            ttHelp.Ext_SetToolTip(helpTagsPerGame, GlobalStrings.DlgAutoCat_Help_ListTagsPerGame);
            ttHelp.Ext_SetToolTip(helpWeightFactor, GlobalStrings.DlgAutoCat_Help_ListWeightFactor);
            ttHelp.Ext_SetToolTip(helpExcludeGenres, GlobalStrings.DlgAutoCat_Help_ListExcludeGenres);

            clbTags.DisplayMember = "text";

            //Hide count column
            lstIncluded.Columns[1].Width = 0;
        }

        #endregion

        #region Delegates

        private delegate void TagItemCallback(ListViewItem obj);

        #endregion

        #region Properties

        private static Database Database => Database.Instance;

        #endregion

        #region Public Methods and Operators

        public void FillTagsList(ICollection<string> preChecked)
        {
            clbTags.Items.Clear();
            loaded = false;

            lstIncluded.Columns[0].Width = -1;
            Dictionary<string, float> tagList = Database.CalculateSortedTagList(list_chkOwnedOnly.Checked ? ownedGames : null, (float) list_numWeightFactor.Value, (int) list_numMinScore.Value, (int) list_numTagsPerGame.Value, list_chkExcludeGenres.Checked, false);
            lstIncluded.BeginUpdate();
            lstIncluded.Items.Clear();
            foreach (KeyValuePair<string, float> tag in tagList)
            {
                ListViewItem newItem = new ListViewItem(string.Format(CultureInfo.CurrentCulture, "{0} [{1:F0}]", tag.Key, tag.Value))
                {
                    Tag = tag.Key
                };

                if (preChecked != null && preChecked.Contains(tag.Key))
                {
                    newItem.Checked = true;
                }

                newItem.SubItems.Add(tag.Value.ToString(CultureInfo.InvariantCulture));
                lstIncluded.Items.Add(newItem);
            }

            lstIncluded.Columns[0].Width = -1;
            SortTags(1, SortOrder.Descending);
            lstIncluded.EndUpdate();

            cmdListRebuild.Text = string.Format(CultureInfo.CurrentCulture, Resources.RebuildListButtonWithCount, lstIncluded.Items.Count);
            loaded = true;
        }

        public override void LoadFromAutoCat(AutoCat autoCat)
        {
            if (!(autoCat is AutoCatTags ac))
            {
                return;
            }

            txtPrefix.Text = ac.Prefix ?? string.Empty;
            numMaxTags.Value = ac.MaxTags;

            list_numMinScore.Value = ac.List_MinScore;
            list_numTagsPerGame.Value = ac.List_TagsPerGame;
            list_chkOwnedOnly.Checked = ac.List_OwnedOnly;
            list_numWeightFactor.Value = (decimal) ac.List_WeightFactor;
            list_chkExcludeGenres.Checked = ac.List_ExcludeGenres;

            FillTagsList(ac.IncludedTags);

            loaded = true;
        }

        public override void SaveToAutoCat(AutoCat autoCat)
        {
            if (!(autoCat is AutoCatTags ac))
            {
                return;
            }

            ac.Prefix = txtPrefix.Text;

            ac.MaxTags = (int) numMaxTags.Value;

            ac.IncludedTags = new HashSet<string>();
            foreach (ListViewItem i in lstIncluded.CheckedItems)
            {
                ac.IncludedTags.Add(i.Tag as string);
            }

            ac.List_MinScore = (int) list_numMinScore.Value;
            ac.List_OwnedOnly = list_chkOwnedOnly.Checked;
            ac.List_TagsPerGame = (int) list_numTagsPerGame.Value;
            ac.List_WeightFactor = (float) list_numWeightFactor.Value;
            ac.List_ExcludeGenres = list_chkExcludeGenres.Checked;
        }

        #endregion

        #region Methods

        private void btnTagSelected_Click(object sender, EventArgs e)
        {
            if (splitTags.Panel1Collapsed)
            {
                splitTags.Panel1Collapsed = false;
                btnTagSelected.Text = "<";
            }
            else
            {
                splitTags.Panel1Collapsed = true;
                btnTagSelected.Text = ">";
            }
        }

        private void clbTags_ItemCheck(object sender, ItemCheckEventArgs e)
        {
            if (e.NewValue == CheckState.Unchecked)
            {
                ((ListViewItem) clbTags.Items[e.Index]).Checked = false;
            }
        }

        private void cmdCheckAll_Click(object sender, EventArgs e)
        {
            SetAllListCheckStates(lstIncluded, true);
        }

        private void cmdListRebuild_Click(object sender, EventArgs e)
        {
            HashSet<string> checkedTags = new HashSet<string>();
            foreach (ListViewItem item in lstIncluded.CheckedItems)
            {
                checkedTags.Add(item.Tag as string);
            }

            FillTagsList(checkedTags);
        }

        private void cmdUncheckAll_Click(object sender, EventArgs e)
        {
            SetAllListCheckStates(lstIncluded, false);
        }

        private void countascendingTags_Click(object sender, EventArgs e)
        {
            SortTags(1, SortOrder.Ascending);
        }

        private void countdescendingTags_Click(object sender, EventArgs e)
        {
            SortTags(1, SortOrder.Descending);
        }

        private void lstIncluded_ItemChecked(object sender, ItemCheckedEventArgs e)
        {
            if (e.Item.Checked)
            {
                clbTags.Items.Add(e.Item, true);
            }
            else if (!e.Item.Checked && loaded)
            {
                workerThread = new Thread(TagItemWorker);
                workerThread.Start(e.Item);
            }

            lblIncluded.Text = string.Format(CultureInfo.CurrentCulture, Resources.IncludedTagsWithCount, clbTags.Items.Count);
        }

        private void nameascendingTags_Click(object sender, EventArgs e)
        {
            SortTags(0, SortOrder.Ascending);
        }

        private void namedescendingTags_Click(object sender, EventArgs e)
        {
            SortTags(0, SortOrder.Descending);
        }

        private void SetAllListCheckStates(ListView list, bool to)
        {
            foreach (ListViewItem item in list.Items)
            {
                item.Checked = to;
            }
        }

        private void SortTags(int c, SortOrder so)
        {
            // Create a comparer.
            lstIncluded.ListViewItemSorter = new ListViewComparer(c, so);

            // Sort.
            lstIncluded.Sort();
        }

        private void TagItem(ListViewItem obj)
        {
            if (clbTags.InvokeRequired)
            {
                TagItemCallback callback = TagItem;
                Invoke(callback, obj);
            }
            else
            {
                clbTags.Items.Remove(obj);
                lblIncluded.Text = string.Format(CultureInfo.CurrentCulture, Resources.IncludedTagsWithCount, clbTags.Items.Count);
            }
        }

        private void TagItemWorker(object obj)
        {
            TagItem((ListViewItem) obj);
        }

        #endregion
    }
}
