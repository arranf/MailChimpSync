using System.Data.Entity.ModelConfiguration;

namespace org.kcionline.MailchimpSync.Model
{
	public class MailChimpPersonAliasConfiguration : EntityTypeConfiguration<MailChimpPersonAlias>
	{
		public MailChimpPersonAliasConfiguration()
		{
			base.HasRequired((MailChimpPersonAlias p) => p.PersonAlias).WithMany().HasForeignKey((MailChimpPersonAlias a) => a.PersonAliasId)
				.WillCascadeOnDelete(true);
			base.HasEntitySetName("MailChimpPersonAlias");
		}
	}
}
