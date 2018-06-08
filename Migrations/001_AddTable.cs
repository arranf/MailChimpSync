using Rock.Plugin;
using System;

namespace org.kcionline.MailchimpSync.Migrations
{
	[MigrationNumber(1, "1.6.0")]
	public class AddTable : Migration
	{
		public override void Up()
		{
			base.Sql("CREATE TABLE [_org_kcionline_MailChimpSync_MailChimpPersonAlias](\r\n    [Id] [int] not null identity(1,1),\r\n    [PersonAliasId] [int],\r\n    [MailChimpUniqueId] [nvarchar(100)],\r\n    [Email] [nvarchar(100)],\r\n\t[LastUpdated] [datetime],\r\n    [CreatedDateTime] [datetime],\r\n    [ModifiedDateTime] [datetime],\r\n    [CreatedByPersonAliasId] [int],\r\n    [ModifiedByPersonAliasId] [int],\r\n    [Guid] uniqueidentifier not null DEFAULT NEWID(),\r\n    [ForeignKey] nvarchar(100) null,\r\n    [ForeignGuid] uniqueidentifier null,\r\n    [ForeignId] uniqueidentifier null\r\n    CONSTRAINT [PK_org_kcionline_MailChimpSync_MailChimpPersonAlias] PRIMARY KEY CLUSTERED ( [Id] ASC )\r\n)\r\nCREATE UNIQUE INDEX [IX_Guid] ON [dbo].[_org_kcionline_MailChimpSync_MailChimpPersonAlias]([Guid])\r\nCREATE INDEX [IX_CreatedByPersonAliasId] ON [dbo].[_org_kcionline_MailChimpSync_MailChimpPersonAlias]([CreatedByPersonAliasId])\r\nCREATE INDEX [IX_ModifiedByPersonAliasId] ON [dbo].[_org_kcionline_MailChimpSync_MailChimpPersonAlias]([ModifiedByPersonAliasId])\r\nCREATE INDEX [IX_ForeignKey] ON [dbo].[_org_kcionline_MailChimpSync_MailChimpPersonAlias] (ForeignKey)\r\nCREATE INDEX [IX_ForeignId] ON [dbo].[_org_kcionline_MailChimpSync_MailChimpPersonAlias] (ForeignId)\r\nCREATE INDEX [IX_ForeignGuid] ON [dbo].[_org_kcionline_MailChimpSync_MailChimpPersonAlias] (ForeignGuid)\r\nCREATE INDEX [IX_PersonAliasId] ON [_org_kcionline_MailChimpSync_MailChimpPersonAlias] ([PersonAliasId])\r\nCREATE INDEX [IX_MailChimpUniqueId] ON [_org_kcionline_MailChimpSync_MailChimpPersonAlias] ([MailChimpUniqueId])");
		}

		public override void Down()
		{
			throw new NotImplementedException();
		}
	}
}
