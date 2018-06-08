using Rock.Plugin;
using System;

namespace org.kcionline.MailchimpSync.Migrations
{
    [MigrationNumber( 3, "1.6.0" )]
    public class AddSecretKey : Migration
    {
        public override void Up()
        {
            base.RockMigrationHelper.AddGlobalAttribute( Rock.SystemGuid.FieldType.TEXT, string.Empty, string.Empty, "MailChimp Secret Key", "Used to ensure unsubscribe events come from MailChimp", 0, string.Empty, "389b5669-5504-44ba-a139-ce6a2447f3d2", "org_kcionline_MailChimpSync_Secret" );
        }

        public override void Down()
        {
            throw new NotImplementedException();
        }
    }
}
