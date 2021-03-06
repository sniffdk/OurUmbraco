﻿using System;
using System.Linq;
using System.Web.UI.WebControls;
using uForum.Businesslogic;
using umbraco.cms.businesslogic.member;

namespace uForum.usercontrols
{
    public partial class ForumSpamListComments : System.Web.UI.UserControl
    {
        protected void Page_Load(object sender, EventArgs e)
        {
            FillSpamCommentGrid();
        }

        protected void NotSpamComment(Object sender, CommandEventArgs e)
        {
            var id = int.Parse(e.CommandArgument.ToString());
            var comment = Comment.GetComment(id, true);
            comment.IsSpam = false;
            comment.Save(true);
            
            // Restore karma
            var member = new Member(comment.MemberId);

            int reputationTotal;
            int.TryParse(member.getProperty("reputationTotal").Value.ToString(), out reputationTotal);
            member.getProperty("reputationTotal").Value = reputationTotal >= 0 ? reputationTotal + 1 : 0;

            int reputationCurrent;
            int.TryParse(member.getProperty("reputationCurrent").Value.ToString(), out reputationCurrent);
            member.getProperty("reputationCurrent").Value = reputationCurrent >= 0 ? reputationCurrent + 1 : 0;
            
            Forum.MarkAsHam(comment.MemberId, comment.Body, "comment");
            FillSpamCommentGrid();
        }

        private void FillSpamCommentGrid()
        {
            var comments = Comment.GetAllSpamComments();
            GridViewSpamComment.DataSource = comments;
            GridViewSpamComment.DataBind();
            GridViewSpamComment.PageIndexChanging += GridViewSpamComment_PageIndexChanging;
        }
        
        protected void GridViewSpamComment_PageIndexChanging(object sender, GridViewPageEventArgs e)
        {
            GridViewSpamComment.PageIndex = e.NewPageIndex;
            var comments = Comment.GetAllSpamComments();
            GridViewSpamComment.DataSource = comments;
            GridViewSpamComment.DataBind();
        }
        
    }
}