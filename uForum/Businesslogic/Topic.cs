﻿using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Web;
using System.Web.UI.MobileControls;
using System.Xml;
using Joel.Net;
using PetaPoco;
using umbraco.cms.businesslogic.member;

namespace uForum.Businesslogic
{
    [PetaPoco.TableName("forumTopics")]
    [PetaPoco.PrimaryKey("id")]
    [PetaPoco.ExplicitColumns]
    public class Topic
    {
        [PetaPoco.Column("id")]
        public int Id { get; private set; }
        [PetaPoco.Column("parentId")]
        public int ParentId { get; private set; }
        [PetaPoco.Column("memberId")]
        public int MemberId { get; private set; }
        [PetaPoco.Column("answer")]
        public int Answer { get; set; }

        [PetaPoco.Column("title")]
        public string Title { get; set; }
        [PetaPoco.Column("urlName")]
        public string UrlName { get; set; }
        [PetaPoco.Column("body")]
        public string Body { get; set; }
        [PetaPoco.Column("created")]
        public DateTime Created { get; private set; }
        [PetaPoco.Column("updated")]
        public DateTime Updated { get; private set; }

        [PetaPoco.Column("isSpam")]
        public bool IsSpam { get; set; }

        private List<Tag> _tags;
        public List<Tag> Tags
        {
            get
            {
                if (_tags == null)
                {
                    _tags = Tag.TagsByTopic(Id);

                }
                return _tags;
            }
            set { _tags = value; }
        }
        private List<Comment> _comments;
        public List<Comment> Comments
        {
            get
            {
                loadComments();
                return _comments;
            }
            set { _comments = value; }
        }

        [PetaPoco.Column("latestReplyAuthor")]
        public int LatestReplyAuthor { get; private set; }

        [PetaPoco.Column("latestComment")]
        public int LatestComment { get; private set; }

        [PetaPoco.Column("replies")]
        public int Replies { get; private set; }
        [PetaPoco.Column("score")]
        public int Score { get; set; }

        [PetaPoco.Column("locked")]
        public bool Locked { get; set; }
        private readonly Events _events = new Events();

        public bool Exists { get { return Id > 0; } }
        
        public bool Editable(int memberId)
        {
            if (Exists == false || memberId == 0)
                return false;

            if (Library.Xslt.IsMemberInGroup("admin", memberId))
                return true;

            return memberId == MemberId;
        }

        public void Move(int newForumId)
        {
            var moveEventArgs = new MoveEventArgs();
            FireBeforeMove(moveEventArgs);

            if (moveEventArgs.Cancel)
                return;

            var newF = new Forum(newForumId);
            var oldF = new Forum(ParentId);

            if (newF.Exists == false)
                return;

            ParentId = newForumId;
            Save(true);

            newF.Save();
            oldF.Save();


            FireAfterMove(moveEventArgs);
        }

        public void Delete()
        {
            var deleteEventArgs = new DeleteEventArgs();
            FireBeforeDelete(deleteEventArgs);

            if (deleteEventArgs.Cancel)
                return;

            var forum = new Forum(ParentId);

            Data.SqlHelper.ExecuteNonQuery("DELETE FROM forumTopics WHERE id = @id", Data.SqlHelper.CreateParameter("@id", Id.ToString(CultureInfo.InvariantCulture)));
            Id = 0;

            forum.Save();

            FireAfterDelete(deleteEventArgs);
        }

        public void MarkAsSpam()
        {
            var markAsSpamEventArgs = new MarkAsSpamEventArgs();
            FireBeforeMarkAsSpam(markAsSpamEventArgs);

            if (markAsSpamEventArgs.Cancel)
                return;

            var forum = new Forum(ParentId);

            var topic = GetTopic(Id);
            var member = new Member(topic.MemberId);
            var akismetApi = Forum.GetAkismetApi();
            var akismetComment = Forum.ConstructAkismetComment(member, "topic", string.Format("{0} - {1}", Title, Body));
            akismetApi.SubmitSpam(akismetComment);

            Data.SqlHelper.ExecuteNonQuery("UPDATE forumTopics SET isSpam = 1 WHERE id = @id", Data.SqlHelper.CreateParameter("@id", Id.ToString(CultureInfo.InvariantCulture)));

            Id = 0;

            forum.Save();

            FireAfterMarkAsSpam(markAsSpamEventArgs);
        }

        public void MarkAsHam()
        {
            var markAsHamEventArgs = new MarkAsHamEventArgs();
            FireBeforeMarkAsHam(markAsHamEventArgs);

            if (markAsHamEventArgs.Cancel)
                return;

            var forum = new Forum(ParentId);

            var topic = GetTopic(Id);
            var member = new Member(topic.MemberId);
            var akismetApi = Forum.GetAkismetApi();
            var akismetComment = Forum.ConstructAkismetComment(member, "topic", string.Format("{0} - {1}", Title, Body));
            akismetApi.SubmitHam(akismetComment);

            Data.SqlHelper.ExecuteNonQuery("UPDATE forumTopics SET isSpam = 0 WHERE id = @id", Data.SqlHelper.CreateParameter("@id", Id.ToString(CultureInfo.InvariantCulture)));

            Id = 0;

            forum.Save();

            // Set reputation to at least 50 so their next posts won't be automatically marked as spam
            int reputation;
            int.TryParse(member.getProperty("reputationTotal").Value.ToString(), out reputation);
            if (reputation < 50)
                member.getProperty("reputationTotal").Value = 50;

            int.TryParse(member.getProperty("reputationCurrent").Value.ToString(), out reputation);
            if (reputation < 50)
                member.getProperty("reputationCurrent").Value = 50;

            member.Save();

            FireAfterMarkAsHam(markAsHamEventArgs);
        }

        public void Lock()
        {
            var lockEventArgs = new LockEventArgs();
            FireBeforeLock(lockEventArgs);

            if (lockEventArgs.Cancel)
                return;

            Data.SqlHelper.ExecuteNonQuery("UPDATE forumTopics SET locked = 1 WHERE id = ", Data.SqlHelper.CreateParameter("@id", Id.ToString(CultureInfo.InvariantCulture)));

            Id = 0;

            FireAfterLock(lockEventArgs);
        }

        public void Save()
        {
            Save(false);
        }

        public void Save(bool silent)
        {
            Save(false, false);
        }

        public void Save(bool silent, bool dontMarkAsSpam)
        {
            if (Id == 0)
            {

                if (Library.Utills.IsMember(MemberId) && !string.IsNullOrEmpty(Title) && !string.IsNullOrEmpty(Body))
                {

                    var createEventArgs = new CreateEventArgs();
                    FireBeforeCreate(createEventArgs);

                    if (createEventArgs.Cancel) 
                        return;


                    UrlName = umbraco.cms.helpers.url.FormatUrl(Title);
                    
                    Data.SqlHelper.ExecuteNonQuery("INSERT INTO forumTopics (parentId, memberId, title, urlName, body, latestReplyAuthor, isSpam) VALUES(@parentId, @memberId, @title, @urlname, @body, @latestReplyAuthor, @isSpam)",
                        Data.SqlHelper.CreateParameter("@parentId", ParentId),
                        Data.SqlHelper.CreateParameter("@memberId", MemberId),
                        Data.SqlHelper.CreateParameter("@title", Title),
                        Data.SqlHelper.CreateParameter("@urlname", UrlName),
                        Data.SqlHelper.CreateParameter("@latestReplyAuthor", LatestReplyAuthor),
                        Data.SqlHelper.CreateParameter("@body", Body),
                        Data.SqlHelper.CreateParameter("@isSpam", dontMarkAsSpam ? false : Forum.IsSpam(MemberId, string.Format("{0} - {1}", Title, Body), "topic", Id))
                        );

                    Created = DateTime.Now;
                    Updated = DateTime.Now;
                    Id = Data.SqlHelper.ExecuteScalar<int>("SELECT MAX(id) FROM forumTopics WHERE memberId = @memberId", Data.SqlHelper.CreateParameter("@memberId", MemberId));

                    var forum = new Forum(ParentId);

                    if (forum.Exists)
                    {
                        forum.SetLatestTopic(Id);
                        forum.SetLatestAuthor(MemberId);
                        forum.LatestPostDate = DateTime.Now;
                        forum.Save();
                    }

                    // save tags
                    Tag.AddTagsToTopic(Id, Tags);

                    FireAfterCreate(createEventArgs);
                }

            }
            else
            {

                var updateEventArgs = new UpdateEventArgs();
                FireBeforeUpdate(updateEventArgs);

                if (updateEventArgs.Cancel == false)
                {
                    var totalComments = Data.SqlHelper.ExecuteScalar<int>("SELECT count(id) from forumComments where (forumComments.isSpam IS NULL OR forumComments.isSpam != 1) AND topicId = @id", Data.SqlHelper.CreateParameter("@id", Id));
                    LatestReplyAuthor = Data.SqlHelper.ExecuteScalar<int>("SELECT TOP 1 memberId FROM forumComments WHERE (forumComments.isSpam IS NULL OR forumComments.isSpam != 1) AND (topicId= @id) ORDER BY Created DESC ", Data.SqlHelper.CreateParameter("@id", Id));
                    LatestComment = Data.SqlHelper.ExecuteScalar<int>("SELECT TOP 1 id FROM forumComments WHERE (forumComments.isSpam IS NULL OR forumComments.isSpam != 1) AND (topicId= @id) ORDER BY Created DESC ", Data.SqlHelper.CreateParameter("@id", Id));

                    UrlName = umbraco.cms.helpers.url.FormatUrl(Title);

                    if (silent == false)
                        Updated = DateTime.Now;

                    Data.SqlHelper.ExecuteNonQuery("UPDATE forumTopics SET replies = @replies, parentId = @parentId, memberId = @memberId, title = @title, urlname = @urlname, body = @body, updated = @updated, locked = @locked, latestReplyAuthor = @latestReplyAuthor, latestComment = @latestComment, isSpam = @isSpam WHERE id = @id",
                        Data.SqlHelper.CreateParameter("@parentId", ParentId),
                        Data.SqlHelper.CreateParameter("@memberId", MemberId),
                        Data.SqlHelper.CreateParameter("@title", Title),
                        Data.SqlHelper.CreateParameter("@urlname", UrlName),
                        Data.SqlHelper.CreateParameter("@body", Body),
                        Data.SqlHelper.CreateParameter("@id", Id),
                        Data.SqlHelper.CreateParameter("@updated", Updated),
                        Data.SqlHelper.CreateParameter("@latestReplyAuthor", LatestReplyAuthor),
                        Data.SqlHelper.CreateParameter("@latestComment", LatestComment),
                        Data.SqlHelper.CreateParameter("@locked", Locked),
                        Data.SqlHelper.CreateParameter("@replies", totalComments),
                        Data.SqlHelper.CreateParameter("@isSpam", dontMarkAsSpam ? false : Forum.IsSpam(MemberId, string.Format("{0} - {1}", Title, Body), "topic", Id))
                    );

                    // save tags
                    Tag.AddTagsToTopic(Id, Tags);

                    UpdateCommentsPosition();

                    FireAfterUpdate(updateEventArgs);
                }
            }
        }

        private void UpdateCommentsPosition()
        {
            const string sql = @"SELECT id, position, created, ROW_NUMBER() OVER (ORDER BY created) AS RowNumber FROM forumComments where (forumComments.isSpam IS NULL OR forumComments.isSpam != 1) AND topicId = @Id";
            var reader = Data.SqlHelper.ExecuteReader(sql, Data.SqlHelper.CreateParameter("@id", Id));

            while (reader.Read())
            {
                var commentId = reader.GetInt("id");
                var rowNumber = reader.GetLong("RowNumber");

                Data.SqlHelper.ExecuteNonQuery("UPDATE forumComments SET position = @position WHERE id = @id", Data.SqlHelper.CreateParameter("@id", commentId), Data.SqlHelper.CreateParameter("@position", rowNumber));
            }

        }

        private void loadComments()
        {
            if (_comments == null)
            {
                _comments = new List<Comment>();
            }

            var db = new Database("umbracoDbDSN");
            foreach (var comment in db.Query<Comment>("Select * from forumComments where (forumComments.isSpam IS NULL OR forumComments.isSpam != 1) AND topicId = @0", Id))
            {
                _comments.Add(comment);
            }


        }

        public XmlNode ToXml(XmlDocument xmlDocument)
        {
            XmlNode topicXml = xmlDocument.CreateElement("topic");

            topicXml.AppendChild(umbraco.xmlHelper.addTextNode(xmlDocument, "title", Title));
            topicXml.AppendChild(umbraco.xmlHelper.addCDataNode(xmlDocument, "body", Body));

            topicXml.AppendChild(umbraco.xmlHelper.addTextNode(xmlDocument, "urlname", UrlName));

            topicXml.AppendChild(umbraco.xmlHelper.addTextNode(xmlDocument, "isSpam", IsSpam.ToString().ToLowerInvariant()));

            // tags
            XmlNode tags = umbraco.xmlHelper.addTextNode(xmlDocument, "tags", "");
            foreach (var tag in Tags)
            {
                var tagNode = umbraco.xmlHelper.addTextNode(xmlDocument, "tag", tag.Name);
                tagNode.Attributes.Append(umbraco.xmlHelper.addAttribute(xmlDocument, "id", tag.Id.ToString()));
                tagNode.Attributes.Append(umbraco.xmlHelper.addAttribute(xmlDocument, "weight", tag.Weight.ToString()));
                tags.AppendChild(tagNode);
                tags.AppendChild(tagNode);
            }
            topicXml.AppendChild(tags);

            if (topicXml.Attributes != null)
            {
                topicXml.Attributes.Append(umbraco.xmlHelper.addAttribute(xmlDocument, "id", Id.ToString(CultureInfo.InvariantCulture)));
                topicXml.Attributes.Append(umbraco.xmlHelper.addAttribute(xmlDocument, "parentId", ParentId.ToString(CultureInfo.InvariantCulture)));
                topicXml.Attributes.Append(umbraco.xmlHelper.addAttribute(xmlDocument, "memberId", MemberId.ToString(CultureInfo.InvariantCulture)));

                topicXml.Attributes.Append(umbraco.xmlHelper.addAttribute(xmlDocument, "latestReplyAuthor", LatestReplyAuthor.ToString(CultureInfo.InvariantCulture)));

                topicXml.Attributes.Append(umbraco.xmlHelper.addAttribute(xmlDocument, "created", Created.ToString(CultureInfo.InvariantCulture)));
                topicXml.Attributes.Append(umbraco.xmlHelper.addAttribute(xmlDocument, "updated", Updated.ToString(CultureInfo.InvariantCulture)));

                topicXml.Attributes.Append(umbraco.xmlHelper.addAttribute(xmlDocument, "locked", Locked.ToString()));
                topicXml.Attributes.Append(umbraco.xmlHelper.addAttribute(xmlDocument, "replies", Replies.ToString(CultureInfo.InvariantCulture)));

                topicXml.Attributes.Append(umbraco.xmlHelper.addAttribute(xmlDocument, "answer", Answer.ToString()));
                topicXml.Attributes.Append(umbraco.xmlHelper.addAttribute(xmlDocument, "score", Score.ToString()));
            }

            return topicXml;
        }

        public static Topic Create(int forumId, string title, string body, int memberId)
        {
            var topic = new Topic
                          {
                              ParentId = forumId,
                              Title = title,
                              Body = body,
                              MemberId = memberId,
                              LatestReplyAuthor = memberId,
                              Replies = 0
                          };
            topic.Save();

            return topic;
        }

        public Topic() { }

        public static Topic GetTopic(int topicId)
        {
            var db = new Database("umbracoDbDSN");
            var topic = db.SingleOrDefault<Topic>(string.Format("SELECT * FROM forumTopics WHERE id = @0"), topicId);
            return topic;
        }
        
        [Obsolete("Use GetTopic instead", false)]
        public Topic(int topicId)
        {
            var topic = GetTopic(topicId);

            Id = topic.Id;
            Answer = topic.Answer;
            Score = topic.Score;
            ParentId = topic.ParentId;
            MemberId = topic.MemberId;
            Replies = topic.Replies;
            Title = topic.Title;
            Body = topic.Body;
            LatestReplyAuthor = topic.LatestReplyAuthor;
            Created = topic.Created;
            Updated = topic.Updated;

            UrlName = topic.UrlName;

            Locked = topic.Locked;

            IsSpam = topic.IsSpam;

            Tags = Tag.TagsByTopic(topicId);
        }

        public static Topic GetFromReader(umbraco.DataLayer.IRecordsReader reader)
        {

            var topic = new Topic
                            {
                                Id = reader.GetInt("id"),
                                ParentId = reader.GetInt("parentId"),
                                MemberId = reader.GetInt("memberId"),
                                Replies = reader.GetInt("replies"),
                                Title = CleanInvalidXmlChars(reader.GetString("title")),
                                Body = CleanInvalidXmlChars(reader.GetString("body")),
                                LatestReplyAuthor = reader.GetInt("latestReplyAuthor"),
                                Created = reader.GetDateTime("created"),
                                Updated = reader.GetDateTime("updated"),
                                UrlName = reader.GetString("urlName"),
                                Locked = reader.GetBoolean("locked")
                            };

            return topic;
        }

        // From: http://forums.asp.net/t/1688373.aspx/1
        private static string CleanInvalidXmlChars(string text)
        {
            // From xml spec valid chars: 
            // #x9 | #xA | #xD | [#x20-#xD7FF] | [#xE000-#xFFFD] | [#x10000-#x10FFFF]     
            // any Unicode character, excluding the surrogate blocks, FFFE, and FFFF. 
            const string regex = @"[^\x09\x0A\x0D\x20-\xD7FF\xE000-\xFFFD\x10000-x10FFFF]";
            return Regex.Replace(text, regex, "");
        }

        //Collections
        public static List<Topic> TopicsInForum(int forumId, int topicsPerPage, int page)
        {
            var topics = new List<Topic>();
            var reader = Data.SqlHelper.ExecuteReader(
                "SELECT TOP @topicsPerPage * FROM forumTopics WHERE (forumTopics.isSpam IS NULL OR forumTopics.isSpam != 1) AND parentId = @parentId ORDER BY updated DESC",
                    Data.SqlHelper.CreateParameter("@topicsPerPage", topicsPerPage.ToString(CultureInfo.InvariantCulture)),
                    Data.SqlHelper.CreateParameter("@topicsPerPage", forumId.ToString(CultureInfo.InvariantCulture)));

            while (reader.Read())
                topics.Add(GetFromReader(reader));

            reader.Close();
            reader.Dispose();

            return topics;
        }

        //Collections
        public static List<Topic> TopicsInForum(int forumId)
        {
            var topics = new List<Topic>();
            var reader = Data.SqlHelper.ExecuteReader("SELECT * FROM forumTopics WHERE (forumTopics.isSpam IS NULL OR forumTopics.isSpam != 1) AND parentId = @parentId ORDER BY updated DESC", Data.SqlHelper.CreateParameter("@parentId", forumId.ToString(CultureInfo.InvariantCulture)));

            while (reader.Read())
                topics.Add(GetFromReader(reader));

            reader.Close();
            reader.Dispose();

            return topics;
        }

        //Collections
        public static List<Topic> Latest(int amount = 10)
        {
            var topics = new List<Topic>();

            // 1057 is the profile node.  This is a hack way of hiding the forums from the latest list on the homepage added by PG
            var reader = Data.SqlHelper.ExecuteReader(
                "SELECT TOP " + amount + " forumTopics.* FROM forumTopics INNER JOIN ForumForums on forumTopics.ParentId = ForumForums.Id Where (forumTopics.isSpam IS NULL OR forumTopics.isSpam != 1) AND forumforums.parentId != 1057 ORDER BY updated DESC");

            while (reader.Read())
            {
                topics.Add(GetFromReader(reader));
            }

            reader.Close();
            reader.Dispose();

            return topics;
        }

        public static List<Topic> GetAll()
        {
            var topics = new List<Topic>();
            var reader = Data.SqlHelper.ExecuteReader("SELECT * FROM forumTopics WHERE (forumTopics.isSpam IS NULL OR forumTopics.isSpam != 1) ");

            while (reader.Read())
                topics.Add(GetFromReader(reader));

            reader.Close();
            reader.Dispose();

            return topics;
        }
        
        public static List<Topic> GetAllSpamTopics()
        {
            using (var db = new Database("umbracoDbDSN"))
            {
                return db.Fetch<Topic>("SELECT * FROM forumTopics WHERE isSpam = 1 ORDER BY id DESC");
            }
        }

        public static int TotalTopics()
        {
            return Data.SqlHelper.ExecuteScalar<int>("SELECT COUNT(id) FROM [forumTopics] WHERE (forumTopics.isSpam IS NULL OR forumTopics.isSpam != 1);");
        }

        /* Events */
        public static event EventHandler<CreateEventArgs> BeforeCreate;

        protected virtual void FireBeforeCreate(CreateEventArgs e)
        {
            _events.FireCancelableEvent(BeforeCreate, this, e);
        }

        public static event EventHandler<CreateEventArgs> AfterCreate;

        protected virtual void FireAfterCreate(CreateEventArgs e)
        {
            if (AfterCreate != null)
                AfterCreate(this, e);
        }

        public static event EventHandler<DeleteEventArgs> BeforeDelete;

        protected virtual void FireBeforeDelete(DeleteEventArgs e)
        {
            _events.FireCancelableEvent(BeforeDelete, this, e);
        }

        public static event EventHandler<DeleteEventArgs> AfterDelete;

        protected virtual void FireAfterDelete(DeleteEventArgs e)
        {
            if (AfterDelete != null)
                AfterDelete(this, e);
        }

        public static event EventHandler<MoveEventArgs> BeforeMove;

        protected virtual void FireBeforeMove(MoveEventArgs e)
        {
            _events.FireCancelableEvent(BeforeMove, this, e);
        }

        public static event EventHandler<MoveEventArgs> AfterMove;

        protected virtual void FireAfterMove(MoveEventArgs e)
        {
            if (AfterMove != null)
                AfterMove(this, e);
        }

        public static event EventHandler<LockEventArgs> BeforeLock;

        protected virtual void FireBeforeLock(LockEventArgs e)
        {
            _events.FireCancelableEvent(BeforeLock, this, e);
        }

        public static event EventHandler<LockEventArgs> AfterLock;

        protected virtual void FireAfterLock(LockEventArgs e)
        {
            if (AfterLock != null)
                AfterLock(this, e);
        }

        public static event EventHandler<UpdateEventArgs> BeforeUpdate;

        protected virtual void FireBeforeUpdate(UpdateEventArgs e)
        {
            _events.FireCancelableEvent(BeforeUpdate, this, e);
        }

        public static event EventHandler<UpdateEventArgs> AfterUpdate;

        protected virtual void FireAfterUpdate(UpdateEventArgs e)
        {
            if (AfterUpdate != null)
                AfterUpdate(this, e);
        }

        public static event EventHandler<MarkAsSpamEventArgs> BeforeMarkAsSpam;

        protected virtual void FireBeforeMarkAsSpam(MarkAsSpamEventArgs e)
        {
            _events.FireCancelableEvent(BeforeMarkAsSpam, this, e);
        }

        public static event EventHandler<MarkAsSpamEventArgs> AfterMarkAsSpam;

        protected virtual void FireAfterMarkAsSpam(MarkAsSpamEventArgs e)
        {
            if (AfterMarkAsSpam != null)
                AfterMarkAsSpam(this, e);
        }
        public static event EventHandler<MarkAsHamEventArgs> BeforeMarkAsHam;

        protected virtual void FireBeforeMarkAsHam(MarkAsHamEventArgs e)
        {
            _events.FireCancelableEvent(BeforeMarkAsHam, this, e);
        }

        public static event EventHandler<MarkAsHamEventArgs> AfterMarkAsHam;

        protected virtual void FireAfterMarkAsHam(MarkAsHamEventArgs e)
        {
            if (AfterMarkAsHam != null)
                AfterMarkAsHam(this, e);
        }
    }
}
