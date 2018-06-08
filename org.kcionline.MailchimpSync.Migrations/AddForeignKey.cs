using Rock.Plugin;
using System;

namespace org.kcionline.MailchimpSync.Migrations
{
	[MigrationNumber(2, "1.6.0")]
	public class AddForeignKey : Migration
	{
		public override void Up()
		{
			base.Sql("ALTER TABLE [_org_kcionline_MailChimpSync_MailChimpPersonAlias]\r\nADD FOREIGN KEY ([PersonAliasId])\r\nREFERENCES [PersonAlias]([Id])");
		}

		public override void Down()
		{
			throw new NotImplementedException();
		}
	}
}
