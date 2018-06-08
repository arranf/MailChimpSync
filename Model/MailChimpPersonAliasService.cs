using Rock.Data;
using System.Linq;

namespace org.kcionline.MailchimpSync.Model
{
	public class MailChimpPersonAliasService : Service<MailChimpPersonAlias>
	{
		public MailChimpPersonAliasService(RockContext context)
			: base((DbContext)context)
		{
		}

		public MailChimpPersonAlias GetByPersonAliasId(int personAliasId)
		{
			return Queryable().FirstOrDefault((MailChimpPersonAlias a) => a.PersonAliasId == personAliasId);
		}

		public MailChimpPersonAlias GetByMailChimpUniqueId(string mailChimpUniqueId)
		{
			return Queryable().FirstOrDefault((MailChimpPersonAlias a) => a.MailChimpUniqueId == mailChimpUniqueId);
		}
	}
}
