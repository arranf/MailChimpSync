using Rock.Data;
using Rock.Model;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Runtime.Serialization;

namespace org.kcionline.MailchimpSync.Model
{
	[Table("_org_kcionline_MailChimpSync_MailChimpPersonAlias")]
	[DataContract]
	public class MailChimpPersonAlias : Model<MailChimpPersonAlias>, IRockEntity, IEntity
	{
		[Required]
		[DataMember(IsRequired = true)]
		public int PersonAliasId
		{
			get;
			set;
		}

		[Required]
		[DataMember(IsRequired = true)]
		public string MailChimpUniqueId
		{
			get;
			set;
		}

		[Required]
		[DataMember(IsRequired = true)]
		public string Email
		{
			get;
			set;
		}

		[Required]
		[DataMember(IsRequired = true)]
		public DateTime LastUpdated
		{
			get;
			set;
		}

		[DataMember]
		[ForeignKey("PersonAliasId")]
		public virtual PersonAlias PersonAlias
		{
			get;
			set;
		}
	}
}
