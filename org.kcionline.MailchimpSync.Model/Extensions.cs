using Rock.Model;
using System;
using System.Linq;
using System.Linq.Expressions;

namespace org.kcionline.MailchimpSync.Model
{
	public static class Extensions
	{
		public static Expression<Func<Person, PersonAlias>> GetPersonAlias()
		{
			return (Person person) => person.Aliases.FirstOrDefault();
		}
	}
}
