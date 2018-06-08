using MailChimp.Net;
using MailChimp.Net.Interfaces;
using MailChimp.Net.Models;
using org.kcionline.MailchimpSync.Model;
using Quartz;
using Rock;
using Rock.Attribute;
using Rock.Data;
using Rock.Model;
using Rock.Web.Cache;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace org.kcionline.MailchimpSync.Jobs
{
    [TextField( "API Key", "The MailChimp API key.", true, "", "", 0, null, false, null )]
    [GroupTypeField( "Group Type", "Groups of this group type will be synced to your MailChimp list", true, "", "", 0, null, "" )]
    [TextField( "List Id", "The MailChimp list to sync to", true, "", "", 0, null, false, null )]
    [IntegerField( "Timeout", "The number of seconds to use before the database connection times out", true, 720, "", 0, null )]
    [DisallowConcurrentExecution]
    public class Sync : IJob
    {
        private const string MERGE_HASH_KEY = "MERGEHASH";
        public const string PERSON_ALIAS_KEY = "PERSONALIA";
        private const string FIRST_NAME_KEY = "FNAME";
        private const string LAST_NAME_KEY = "LNAME";
        private const string AGE_KEY = "AGE";
        private const string DATE_OF_BIRTH_KEY = "DOB";
        private int _syncCount = 0;
        private int _timeout;
        private string _listId;
        private IMailChimpManager _manager;
        private int _groupTypeId;
        private Guid? _workflowTypeGuid;
        private List<Exception> _exceptions = new List<Exception>();


        public void Execute( IJobExecutionContext context )
        {
            var jobDataMap = context.JobDetail.JobDataMap;
            string apiKey = jobDataMap.GetString( "APIKey" );
            var groupTypeGuid = jobDataMap.GetString( "GroupType" ).AsGuidOrNull();
            _listId = jobDataMap.GetString( "ListId" );
            _timeout = jobDataMap.GetIntFromString( "Timeout" );
            _manager = new MailChimpManager( apiKey );
            _groupTypeId = ValidateParameters( apiKey, groupTypeGuid );
            _workflowTypeGuid = jobDataMap.GetString( "NewPersonWorkflow" ).AsGuidOrNull();

            // TODO Groups Sync and Segments Sync

            IEnumerable<ListSegment> segments;
            try
            {
                segments = GetSegments().Result;
            }
            catch (Exception e)
            {
                throw new Exception( "Unable to fetch Mailchimp segments", e );
            }


            IEnumerable<Member> mailChimpMembers;
            try
            {
                mailChimpMembers = GetListMembers().Result;
            }
            catch ( Exception innerException )
            {
                throw new Exception( "Unable to fetch Mailchimp list members", innerException );
            }

            // Find who's on the list and update anyone if their mergefields would be different
            HashSet<int> existingPersonAliasIds = SyncFromMailChimp( mailChimpMembers );

            // Get all people who should be synced
            var rockContext = GenerateRockContext();
            var groups = new GroupService( rockContext )
                                .Queryable( "Members.Select(l1 => l1.Person)" )
                                .AsNoTracking()
                                .Where( g => g.GroupTypeId == _groupTypeId );

            var segmentNamesToId = segments.Select( s => new { s.Name, s.Id } ).ToDictionary( a => a.Name, s => s.Id );
            var groupNamesByID = groups.Select( g => new { g.Name, g.Id } ).ToDictionary(a => a.Id, g => g.Name);


            int activeRecordStatusValueId = DefinedValueCache.Read( Rock.SystemGuid.DefinedValue.PERSON_RECORD_STATUS_ACTIVE.AsGuid(), null ).Id;
            var peopleByGroup = groups
                .SelectMany( g => g.Members )
                .Where( p => !p.Person.IsDeceased && p.Person.RecordStatusValueId == activeRecordStatusValueId && p.Person.IsEmailActive && p.Person.Email != null && p.Person.Email != String.Empty && p.Person.EmailPreference == EmailPreference.EmailAllowed )
                .GroupBy( gm => gm.GroupId, gm => gm.Person );

            // Sync anyone missing
            SyncToMailChimp( existingPersonAliasIds, peopleByGroup );

            // Ensure segments are updated
            SyncSegments( groupNamesByID, peopleByGroup, segmentNamesToId ).Wait();

            context.Result = string.Format( "Synced a total of {0} people", _syncCount );

            if ( _exceptions.Any() )
            {
                throw new AggregateException( "One or more person syncs failed ", _exceptions );
            }
        }

        private async Task SyncSegments( Dictionary<int, string> groupNamesByID, IQueryable<IGrouping<int, Person>> peopleByGroup, Dictionary<string, int> segments )
        {
            foreach (var keyPair in groupNamesByID)
            {
                String groupName = keyPair.Value;
                IEnumerable<Person> people = peopleByGroup.Where( g => g.Key == keyPair.Key ).SelectMany( a => a.Select( b => b ) );


                // update
                if ( segments.Keys.Contains(groupName))
                {
                    try
                    {

                        await UpdateSegment( groupName, segments[groupName], people );
                    } catch (Exception e)
                    {
                        throw new Exception( "Error updating segment", e );
                    }
                }
                else //create
                {
                    try
                    {
                        await AddSegment( groupName, people );
                    } catch (Exception e)
                    {
                        throw new Exception( "Error adding segment " + groupName, e );
                    }
                }
            }
        }

        private HashSet<int> SyncFromMailChimp( IEnumerable<Member> listMembers )
        {
            RockContext rockContext = GenerateRockContext();
            MailChimpPersonAliasService mailChimpPersonAliasService = new MailChimpPersonAliasService( rockContext );
            var personService = new PersonService( rockContext );
            HashSet<int> seenPesonAliasIds = new HashSet<int>();
            int count = 0;
            int recordTypeId = DefinedValueCache.Read( Rock.SystemGuid.DefinedValue.PERSON_RECORD_TYPE_PERSON.AsGuid() ).Id;
            int recordStatusId = DefinedValueCache.Read( Rock.SystemGuid.DefinedValue.PERSON_RECORD_STATUS_ACTIVE.AsGuid() ).Id;
            int connectionStatusValueId = DefinedValueCache.Read( Rock.SystemGuid.DefinedValue.PERSON_CONNECTION_STATUS_WEB_PROSPECT.AsGuid() ).Id;
            foreach ( Member listMember in listMembers )
            {
                count++;
                if ( count > 200 )
                {
                    count = 0;
                    rockContext.SaveChanges();
                    rockContext = GenerateRockContext();
                    mailChimpPersonAliasService = new MailChimpPersonAliasService( rockContext );
                    personService = new PersonService( rockContext );
                }
                MailChimpPersonAlias mailChimpPersonAlias = null;
                // Get by unique ID if no personaliasid seen, else use personaliasid
                mailChimpPersonAlias = ( ( !listMember.MergeFields.ContainsKey( PERSON_ALIAS_KEY ) || !listMember.MergeFields[PERSON_ALIAS_KEY].ToString().AsIntegerOrNull().HasValue ) ? mailChimpPersonAliasService.GetByMailChimpUniqueId( listMember.UniqueEmailId ) : mailChimpPersonAliasService.GetByPersonAliasId( listMember.MergeFields[PERSON_ALIAS_KEY].ToString().AsInteger() ) );

                // TODO Some kind of check they're in the group (?)


                if ( mailChimpPersonAlias == null )
                {
                    // Can't find a match in our database, guess we better try and find or create a person
                    mailChimpPersonAlias = CreatePerson( rockContext, personService, recordTypeId, recordStatusId, connectionStatusValueId, listMember );
                    if (_workflowTypeGuid.HasValue)
                    {
                        LaunchWorkflow( mailChimpPersonAlias.PersonAlias.Person );
                    }
                }
                else
                {

                    // Handle change of email address
                    var person = mailChimpPersonAlias.PersonAlias.Person;
                    if ( person.Email != mailChimpPersonAlias.Email )
                    {
                        RemoveFromMailChimp( mailChimpPersonAlias ).Wait();
                        try
                        {
                            AddOrUpdatePerson( person, rockContext );
                        }
                        catch ( Exception e )
                        {
                            _exceptions.Add( new Exception( "Failed to add or update person", e ) );
                        }
                    }
                    else
                    {
                        // Check to see if person has been updated
                        var mergeFields = CreateMergeFields( person );
                        string mergeFieldsHash = HashDictionary( mergeFields );
                        if ( !listMember.MergeFields.ContainsKey( MERGE_HASH_KEY ) || listMember.MergeFields[MERGE_HASH_KEY].ToString() != mergeFieldsHash )
                        {
                            try
                            {
                                AddOrUpdatePerson( mailChimpPersonAlias.PersonAlias.Person, rockContext );
                            }
                            catch ( Exception e )
                            {
                                _exceptions.Add( new Exception( "Failed to add or update person", e ) );
                            }
                        }
                    }

                }
                seenPesonAliasIds.Add( mailChimpPersonAlias.PersonAliasId );
            }
            return seenPesonAliasIds;
        }

        private MailChimpPersonAlias CreatePerson( RockContext rockContext, PersonService personService, int recordTypeId, int recordStatusId, int connectionStatusValueId, Member listMember )
        {
            MailChimpPersonAlias mailChimpPersonAlias;
            Person person;
            person = personService.GetByEmail( listMember.EmailAddress ).FirstOrDefault();
            if ( person == null )
            {
                string firstName = listMember.MergeFields[FIRST_NAME_KEY].ToString();
                string lastName = listMember.MergeFields[LAST_NAME_KEY].ToString();
                person = new Person
                {
                    IsSystem = false,
                    RecordTypeValueId = recordTypeId,
                    RecordStatusValueId = recordStatusId,
                    FirstName = ( firstName.IsNotNullOrWhitespace() ? firstName : "Unknown" ),
                    LastName = ( lastName.IsNotNullOrWhitespace() ? lastName : "Unknown" ),
                    Email = listMember.EmailAddress,
                    IsEmailActive = true,
                    EmailPreference = EmailPreference.EmailAllowed,
                    ConnectionStatusValueId = connectionStatusValueId
                };
                PersonService.SaveNewPerson( person, rockContext, null, false );
            }
            mailChimpPersonAlias = AddOrUpdatePerson( person, rockContext );
            return mailChimpPersonAlias;
        }

        private void SyncToMailChimp( HashSet<int> existingPersonAliasIds, IQueryable<IGrouping<int, Person>> peopleGroupedByGroupId  )
        {
            RockContext rockContext = GenerateRockContext();
            var validGroupMembers = peopleGroupedByGroupId
                            .SelectMany(g => g.Select(p => p))
                            .ToList();

            var peopleNotOnList = validGroupMembers.Where( p => p.PrimaryAliasId.HasValue && !existingPersonAliasIds.Contains( p.PrimaryAliasId.Value ) );
            MailChimpPersonAliasService mailChimpPersonAliasService = new MailChimpPersonAliasService( rockContext );
            foreach ( var person in peopleNotOnList )
            {
                // TODO Make a batch job
                try
                {
                    AddOrUpdatePerson( person, rockContext );
                }
                catch ( Exception e )
                {
                    _exceptions.Add( new Exception( "Failed to add or update person", e ) );
                }
            }
        }

        private RockContext GenerateRockContext()
        {
            RockContext rockContext = new RockContext();
            rockContext.Database.CommandTimeout = _timeout;
            return rockContext;
        }

        private int ValidateParameters( string apiKey, Guid? groupTypeGuid )
        {
            if ( apiKey.IsNullOrWhiteSpace() )
            {
                throw new Exception( "No API key is set. Unable to sync." );
            }
            if ( _listId.IsNullOrWhiteSpace() )
            {
                throw new Exception( "No MailChimp List ID set. Unable to sync." );
            }
            if ( !groupTypeGuid.HasValue )
            {
                throw new Exception( "No Group Type set. Unable to sync." );
            }
            GroupTypeCache groupTypeCache = GroupTypeCache.Read( groupTypeGuid.Value, null );
            if ( groupTypeCache == null )
            {
                throw new Exception( "Invalid group type set. Could not be found. Unable to sync" );
            }
            return groupTypeCache.Id;
        }

        private async Task<IEnumerable<Member>> GetListMembers()
        {
            return await _manager.Members.GetAllAsync( _listId, null ).ConfigureAwait( false );
        }

        private MailChimpPersonAlias AddOrUpdatePerson( Person person, RockContext rockContext )
        {
            try
            {
                MailChimpPersonAliasService mailChimpPersonAliasService = new MailChimpPersonAliasService( rockContext );
                Member member = MakeMailChimpMember( person );
                member = AddOrUpdateMailChimpMember( member ).Result;
                if ( person.PrimaryAliasId.HasValue )
                {
                    MailChimpPersonAlias mailChimpPersonAlias = mailChimpPersonAliasService.GetByPersonAliasId( person.PrimaryAliasId.Value );
                    if ( mailChimpPersonAlias != null && mailChimpPersonAlias.LastUpdated >= RockDateTime.Now.AddMinutes(-5))
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
                    _syncCount++;
                    return mailChimpPersonAlias;
                }
                throw new Exception( "No person primary alias found" );
            }
            catch ( Exception e )
            {
                throw new Exception( "Error adding or updating a person", e );
            }
        }

        private async Task RemoveFromMailChimp( MailChimpPersonAlias mailChimpPersonAlias )
        {
            await _manager.Members.DeleteAsync( _listId, mailChimpPersonAlias.Email ).ConfigureAwait( false );
        }

        private static Member MakeMailChimpMember( Person person )
        {
            Dictionary<string, object> mergeFields = CreateMergeFields( person );
            return new Member
            {
                EmailAddress = person.Email,
                StatusIfNew = Status.Subscribed,
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
            var address = person.GetFamily( null )?.GroupLocations?.FirstOrDefault( ( GroupLocation a ) => a.IsMailingLocation )?.Location;
            mergeFields.Add( "STREET1", address?.Street1.ToStringSafe() );
            mergeFields.Add( "STREET2", address?.Street2.ToStringSafe() );
            mergeFields.Add( "CITY", address?.City.ToStringSafe() );
            mergeFields.Add( "POSTALCODE", address?.PostalCode.ToStringSafe() );
            mergeFields.Add( "COUNTRY", address?.Country.ToStringSafe() );

            //mergeFields.Add( "GROUPS", String.Join( ",", new GroupMemberService( new RockContext() ).GetByPersonId( person.Id ).Select( a => a.GroupId ).OrderBy( b => b ).Select( c => c.ToString() ).ToArray() ) );
            // TODO
            //mergeFields.Add( "LINE", string.Empty );
            mergeFields.Add( MERGE_HASH_KEY, HashDictionary( mergeFields ) );
            return mergeFields;
        }

        private async Task<Member> AddOrUpdateMailChimpMember( Member member )
        {
            return await _manager.Members.AddOrUpdateAsync( _listId, member ).ConfigureAwait( false );
        }

        private static string HashDictionary( Dictionary<string, object> dictionary )
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

        private void LaunchWorkflow(Person person )
        {
            // don't launch a workflow if no adult is present in the family

            using ( var rockContext = new RockContext() )
            {
                var workflowType = WorkflowTypeCache.Read( _workflowTypeGuid.Value );
                if ( workflowType != null && ( workflowType.IsActive ?? true ) )
                {
                    var workflowService = new WorkflowService( rockContext );
                    var workflow = Workflow.Activate( workflowType, person.FullName, rockContext );
                    workflowService.Add( workflow );
                    rockContext.SaveChanges();

                    workflow.SetAttributeValue( "Person", person.Guid );
                    workflow.SaveAttributeValues();
                }
            }
        }

        private async Task<IEnumerable<ListSegment>> GetSegments()
        {
            return await _manager.ListSegments.GetAllAsync( _listId, null ).ConfigureAwait( false );
        }

        private async Task<ListSegment> AddSegment(String groupName, IEnumerable<Person> people)
        {
            var segment = new MailChimp.Net.Core.Segment();
            segment.Name = groupName;
            segment.EmailAddresses = people.Select( p => p.Email );
            return await _manager.ListSegments.AddAsync( _listId, segment );
        }

        private async Task<ListSegment> UpdateSegment( String groupName, int segmentId, IEnumerable<Person> people )
        {
            var segment = new MailChimp.Net.Core.Segment();
            segment.Name = groupName;
            segment.EmailAddresses = people.Select( p => p.Email );
            return await _manager.ListSegments.UpdateAsync( _listId, segmentId.ToString(), segment );
        }
    }
}
