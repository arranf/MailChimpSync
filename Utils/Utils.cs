using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using API;
using MailChimp.Net.Models;
using org.kcionline.bricksandmortarstudio.Extensions;
using org.kcionline.MailchimpSync.Model;
using Rock;
using Rock.Data;
using Rock.Model;

namespace Service
{
    public class Utils
    {
        public const string MERGE_HASH_KEY = "MERGEHASH";
        public const string PERSON_ALIAS_KEY = "PERSONALIA";
        public const string FIRST_NAME_KEY = "FNAME";
        public const string LAST_NAME_KEY = "LNAME";
        public const string AGE_KEY = "AGE";
        public const string DATE_OF_BIRTH_KEY = "DOB";
        public const string GENDER_KEY = "GENDER";
        public const string CITY_KEY = "CITY";
        public const string LEADER_KEY = "LEADER";
        public const string MEMBERS_KEY = "MEMBERS";

        public static bool IsSyncNeeded(Person person, MailChimp.Net.Models.Member listMember)
        {
            var mergeFields = CreateMergeFields( person );
            string mergeFieldsHash = HashDictionary( mergeFields );
            return !listMember.MergeFields.ContainsKey( MERGE_HASH_KEY ) || listMember.MergeFields[MERGE_HASH_KEY].ToString() != mergeFieldsHash;
        }

        public static MailChimpPersonAlias AddOrUpdatePerson(MailChimpApi api, Person person, RockContext rockContext )
        {
            try
            {
                MailChimpPersonAliasService mailChimpPersonAliasService = new MailChimpPersonAliasService( rockContext );
                Member member = MakeMailChimpMember( person );
                member = api.AddOrUpdateMailChimpMember( member ).Result;
                if ( person.PrimaryAliasId.HasValue )
                {
                    MailChimpPersonAlias mailChimpPersonAlias = mailChimpPersonAliasService.GetByPersonAliasId( person.PrimaryAliasId.Value );
                    if ( mailChimpPersonAlias != null && mailChimpPersonAlias.LastUpdated >= RockDateTime.Now.AddMinutes( -5 ) )
                    {
                        // Skip sync, synced recently
                        return mailChimpPersonAlias;
                    }

                    if ( mailChimpPersonAlias == null )
                    {
                        mailChimpPersonAlias = new MailChimpPersonAlias();
                        mailChimpPersonAlias.PersonAliasId = person.PrimaryAliasId.Value;
                    }
                    mailChimpPersonAlias.Email = member.EmailAddress;
                    mailChimpPersonAlias.MailChimpUniqueId = member.UniqueEmailId;
                    mailChimpPersonAlias.LastUpdated = RockDateTime.Now;
                    if ( mailChimpPersonAlias.Id == 0 )
                    {
                        mailChimpPersonAliasService.Add( mailChimpPersonAlias );
                    }
                    rockContext.SaveChanges();
                    return mailChimpPersonAlias;
                }
                throw new Exception( "No person primary alias found" );
            }
            catch ( Exception e )
            {
                throw new Exception( "Error adding or updating a person", e );
            }
        }

        public static Member MakeMailChimpMember( Person person )
        {
            Dictionary<string, object> mergeFields = CreateMergeFields( person );
            return new Member
            {
                EmailAddress = person.Email,
                StatusIfNew = Status.Subscribed,
                Status = Status.Subscribed,
                MergeFields = mergeFields
            };
        }

        private static Dictionary<string, object> CreateMergeFields( Person person )
        {
            Dictionary<string, object> mergeFields = new Dictionary<string, object>();
            mergeFields.Add( FIRST_NAME_KEY, person.NickName.ToStringSafe() );
            mergeFields.Add( LAST_NAME_KEY, person.LastName.ToStringSafe() );
            mergeFields.Add( PERSON_ALIAS_KEY, person.PrimaryAliasId.ToStringSafe() );
            mergeFields.Add( AGE_KEY, person.Age.ToStringSafe() );
            mergeFields.Add( DATE_OF_BIRTH_KEY, person.BirthDate.HasValue ? person.BirthDate.Value.ToString( "yyyy-MM-dd" ) : String.Empty );
            mergeFields.Add( GENDER_KEY, person.Gender.ToStringSafe() );
            var address = person.GetFamily( null )?.GroupLocations?.FirstOrDefault( ( GroupLocation a ) => a.IsMailingLocation )?.Location;
            mergeFields.Add( CITY_KEY, address?.City.ToStringSafe() );

            // TODO Use an existing Rock Context
            var primaryGroup = person.GetPersonsPrimaryKciGroup( new RockContext() );
            var leaderFullName = primaryGroup?.Members.FirstOrDefault()?.Person;
            var memberNames = primaryGroup?.Members.Select( m => m.Person.FullName );

            mergeFields.Add( LEADER_KEY, leaderFullName.ToStringSafe() );
            mergeFields.Add( MEMBERS_KEY, memberNames.ToStringSafe() );
            mergeFields.Add( MERGE_HASH_KEY, HashDictionary( mergeFields ) );
            return mergeFields;
        }

        public static string HashDictionary( Dictionary<string, object> dictionary )
        {
            // Build concat string of mergefield
            SortedDictionary<string, object> sortedDictionary = new SortedDictionary<string, object>( dictionary );
            StringBuilder stringBuilder = new StringBuilder();
            foreach ( KeyValuePair<string, object> item in sortedDictionary )
            {
                if ( !( item.Key == MERGE_HASH_KEY ) )
                {
                    stringBuilder.Append( item.Key );
                    stringBuilder.Append( "+" );
                    stringBuilder.Append( item.Value.ToStringSafe() );
                    stringBuilder.Append( "|" );
                }
            }
            string mergeFieldsConcat = stringBuilder.ToString();
            stringBuilder.Clear();

            // Hash
            using ( MD5 mD = MD5.Create() )
            {
                byte[] bytes = Encoding.UTF8.GetBytes( mergeFieldsConcat );
                byte[] array = mD.ComputeHash( bytes );
                for ( int i = 0; i < array.Length; i++ )
                {
                    stringBuilder.Append( array[i].ToString( "X2" ) );
                }
                return stringBuilder.ToString();
            }
        }

    }
}
