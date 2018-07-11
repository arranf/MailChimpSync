
using MailChimp.Net.Models;
using org.kcionline.MailchimpSync.Model;
using Quartz;
using Rock;
using Rock.Attribute;
using Rock.Data;
using Rock.Model;
using Rock.Web.Cache;
using org.kcionline.MailchimpSync.Utils;
using org.kcionline.MailchimpSync.API;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;

namespace org.kcionline.MailchimpSync.Jobs
{
    [TextField( "API Key", "The MailChimp API key.", true, "", "", 0, null, false, null )]
    [GroupTypeField( "Group Type", "Groups of this group type will be synced to your MailChimp list", true, "", "", 0, null, "" )]
    [TextField( "List Id", "The MailChimp list to sync to", true, "", "", 0, null, false, null )]
    [IntegerField( "Timeout", "The number of seconds to use before the database connection times out", true, 720, "", 0, null )]

    [WorkflowTypeField( "New Person Workflow", "The workflow type to launch when a new person is added.", key: "NewPersonWorkflow", order: 0 )]
    [DefinedValueField(Rock.SystemGuid.DefinedType.PERSON_CONNECTION_STATUS, "New Person Connection Status", "The connection status for new people", true, key: "NewPersonConnectionStatus", defaultValue: Rock.SystemGuid.DefinedValue.PERSON_CONNECTION_STATUS_WEB_PROSPECT)]

    [DisallowConcurrentExecution]
    public class Sync : IJob
    {
        private MailChimpApi _api;
        private int _syncCount = 0;
        private int _timeout;
        private string _listId;
        private int _groupTypeId;
        private Guid? _workflowTypeGuid;
        private Guid _newPersonConnectionStatusGuid;
        private List<Exception> _exceptions = new List<Exception>();


        public void Execute( IJobExecutionContext context )
        {
            var jobDataMap = context.JobDetail.JobDataMap;
            string apiKey = jobDataMap.GetString( "APIKey" );
            var groupTypeGuid = jobDataMap.GetString( "GroupType" ).AsGuidOrNull();
            _listId = jobDataMap.GetString( "ListId" );
            _timeout = jobDataMap.GetIntFromString( "Timeout" );
            _workflowTypeGuid = jobDataMap.GetString( "NewPersonWorkflow" ).AsGuidOrNull();
            _newPersonConnectionStatusGuid = jobDataMap.GetString( "NewPersonConnectionStatus" ).AsGuid();
            _groupTypeId = ValidateParameters( apiKey, groupTypeGuid );

            _api = new MailChimpApi( apiKey, _listId );

            // Get segments
            IEnumerable<ListSegment> segments;
            try
            {
                segments = _api.GetSegments().Result;
            }
            catch ( Exception e )
            {
                throw new Exception( "Unable to fetch Mailchimp segments", e );
            }

            // Get list members
            IEnumerable<Member> mailChimpMembers;
            try
            {
                mailChimpMembers = _api.GetListMembers().Result;
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
                                .Where( g => g.GroupTypeId == _groupTypeId && g.IsActive );

            var segmentIdsByName = segments.Select( s => new { s.Name, s.Id } ).ToDictionary( a => a.Name, s => s.Id );
            var groupNamesByID = groups.Select( g => new { g.Name, g.Id } ).ToDictionary( a => a.Id, g => g.Name );


            int activeRecordStatusValueId = DefinedValueCache.Read( Rock.SystemGuid.DefinedValue.PERSON_RECORD_STATUS_ACTIVE.AsGuid(), null ).Id;
            var peopleByGroup = groups
                .SelectMany( g => g.Members )
                .Where( p => !p.Person.IsDeceased && p.Person.RecordStatusValueId == activeRecordStatusValueId && p.Person.IsEmailActive && p.Person.Email != null && p.Person.Email != String.Empty && p.Person.EmailPreference == EmailPreference.EmailAllowed )
                .GroupBy( gm => gm.GroupId, gm => gm.Person );

            // Sync anyone missing
            SyncToMailChimp( existingPersonAliasIds, peopleByGroup );

            // Ensure segments are updated
            SyncSegments( groupNamesByID, peopleByGroup, segmentIdsByName ).Wait();

            context.Result = string.Format( "Synced a total of {0} people", _syncCount );

            if ( _exceptions.Any() )
            {
                throw new AggregateException( "One or more syncs failed ", _exceptions );
            }
        }

        private async Task SyncSegments( Dictionary<int, string> groupNamesByID, IQueryable<IGrouping<int, Person>> peopleByGroup, Dictionary<string, int> segments )
        {
            foreach ( var keyPair in groupNamesByID )
            {
                String groupName = keyPair.Value;
                int groupId = keyPair.Key;
                // Name-Id
                string newSegmentName = SegmentNameFromGroupandId( groupName, groupId );
                IEnumerable<Person> people = peopleByGroup
                    .Where( g => g.Key == groupId )
                    .SelectMany( a => a.Select( b => b ) );
                
                string existingSegmentName = segments.Keys.FirstOrDefault( g => g.Contains('-') && GroupIdFromSegmentName( g ) == groupId );
                // update
                if ( existingSegmentName.IsNotNullOrWhitespace() )
                {
                    // This will also rename the segment if need be
                    try
                    {

                        await _api.UpdateSegment( newSegmentName, segments[existingSegmentName], people );
                    }

                    catch ( Exception e )
                    {
                        _exceptions.Add( new Exception( "Error updating segment", e ) );
                        continue;
                    }
                }

                // Segment doesn't exist so create it 
                try
                {
                    await _api.AddSegment( newSegmentName, people );
                }
                catch ( Exception e )
                {
                    _exceptions.Add( new Exception( "Error adding segment " + existingSegmentName, e ) );
                }
            }

            // Clean up any old segments (i.e. not formatted correctly or groups is deleted)
            var groupIds = new HashSet<int>( groupNamesByID.Keys );
            var segmentsToDelete = segments.Keys.Where( s => !s.Contains( '-' ) || !groupIds.Contains( GroupIdFromSegmentName( s ) ) ).Select(s => segments[s]);
            foreach (var segmentId in segmentsToDelete)
            {
                try
                {
                    await _api.DeleteSegment( segmentId );
                }
                catch ( Exception e )
                {
                    _exceptions.Add( new Exception( "Error removing segment " + segmentId, e ) );
                }
            }
        }

        private static string SegmentNameFromGroupandId( string groupName, int groupId )
        {
            return string.Format( "{0}-{1}", groupName, groupId );
        }

        private static int GroupIdFromSegmentName( string newSegmentName )
        {
            var lastIndex = newSegmentName.LastIndexOf( '-' );
            return newSegmentName.Substring( lastIndex + 1).AsInteger();
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
            int connectionStatusValueId = DefinedValueCache.Read( _newPersonConnectionStatusGuid ).Id;
            foreach ( Member listMember in listMembers.Where(l => l.Status != Status.Undefined && l.Status != Status.Unsubscribed) )
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
                mailChimpPersonAlias = ( ( !listMember.MergeFields.ContainsKey( Utils.PERSON_ALIAS_KEY ) || !listMember.MergeFields[Utils.PERSON_ALIAS_KEY].ToString().AsIntegerOrNull().HasValue ) ? mailChimpPersonAliasService.GetByMailChimpUniqueId( listMember.UniqueEmailId ) : mailChimpPersonAliasService.GetByPersonAliasId( listMember.MergeFields[Utils.PERSON_ALIAS_KEY].ToString().AsInteger() ) );

                if ( mailChimpPersonAlias == null)
                {

                    // Can't find a match in our database, guess we better try and find or create a person
                    if ( _workflowTypeGuid.HasValue )
                    {
                        // defer to workflow
                        LaunchWorkflow( listMember.MergeFields[Utils.FIRST_NAME_KEY].ToString(), listMember.MergeFields[Utils.LAST_NAME_KEY].ToString(), listMember.EmailAddress);
                    }
                    else
                    {
                        // do it ourselves
                        mailChimpPersonAlias = CreatePerson( rockContext, personService, recordTypeId, recordStatusId, connectionStatusValueId, listMember );
                    }
                }
                else
                {
                    var person = mailChimpPersonAlias.PersonAlias.Person;
                    
                    if (person.EmailPreference != EmailPreference.EmailAllowed)
                    {
                        _api.RemoveFromMailChimp( mailChimpPersonAlias ).Wait();
                    }
                    // Handle change of email address, Rock always wins
                    else if ( person.Email != mailChimpPersonAlias.Email )
                    {
                        _api.RemoveFromMailChimp( mailChimpPersonAlias ).Wait();
                        try
                        {
                            Utils.AddOrUpdatePerson(_api, person, rockContext );
                            _syncCount++;
                        }
                        catch ( Exception e )
                        {
                            _exceptions.Add( new Exception( "Failed to add or update person", e ) );
                        }
                    }
                    else
                    {
                        // Check to see if person has been updated
                        if ( Utils.IsSyncNeeded(person, listMember) )
                        {
                            try
                            {
                                Utils.AddOrUpdatePerson(_api, mailChimpPersonAlias.PersonAlias.Person, rockContext );
                                _syncCount++;
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
                string firstName = listMember.MergeFields[Utils.FIRST_NAME_KEY].ToString();
                string lastName = listMember.MergeFields[Utils.LAST_NAME_KEY].ToString();
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
            mailChimpPersonAlias = Utils.AddOrUpdatePerson(_api, person, rockContext );
            _syncCount++;
            return mailChimpPersonAlias;
        }

        private void SyncToMailChimp( HashSet<int> existingPersonAliasIds, IQueryable<IGrouping<int, Person>> peopleGroupedByGroupId )
        {
            RockContext rockContext = GenerateRockContext();
            var validGroupMembers = peopleGroupedByGroupId
                            .SelectMany( g => g.Select( p => p ) )
                            .ToList();

            var peopleNotOnList = validGroupMembers.Where( p => p.PrimaryAliasId.HasValue && !existingPersonAliasIds.Contains( p.PrimaryAliasId.Value ) );
            MailChimpPersonAliasService mailChimpPersonAliasService = new MailChimpPersonAliasService( rockContext );
            foreach ( var person in peopleNotOnList )
            {
                // TODO Make a batch job
                try
                {
                    Utils.AddOrUpdatePerson(_api, person, rockContext );
                    _syncCount++;
                }
                catch ( Exception e )
                {
                    _exceptions.Add( new Exception( string.Format("Failed to add or update person {0}", person.FullName), e ) );
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
            

            if ( _newPersonConnectionStatusGuid.IsEmpty())
            {
                throw new Exception( "New person connection status could not be found. Unable to sync." );
            }
            var connectionStatusCached = DefinedValueCache.Read( _newPersonConnectionStatusGuid );
            if (connectionStatusCached == null)
            {

                throw new Exception( "New person connection status could not be found. Unable to sync." );
            }

            var groupTypeCached = GroupTypeCache.Read( groupTypeGuid.Value );
            if ( groupTypeCached == null )
            {
                throw new Exception( "Invalid group type set. Could not be found. Unable to sync" );
            }
            return groupTypeCached.Id;
        }

       
        private void LaunchWorkflow( Person person )
        {
            using ( var rockContext = new RockContext() )
            {
                var workflowType = WorkflowTypeCache.Read( _workflowTypeGuid.Value );
                if ( workflowType != null && ( workflowType.IsActive ?? true ) )
                {
                    var workflowService = new WorkflowService( rockContext );
                    var workflow = Rock.Model.Workflow.Activate( workflowType, person.FullName, rockContext );
                    workflowService.Add( workflow );
                    rockContext.SaveChanges();

                    workflow.SetAttributeValue( "Person", person.Guid );
                    workflow.SaveAttributeValues();
                }
            }
        }

        private void LaunchWorkflow( String firstName, String lastName, String email)
        {
            using ( var rockContext = new RockContext() )
            {
                var workflowType = WorkflowTypeCache.Read( _workflowTypeGuid.Value );
                if ( workflowType != null && ( workflowType.IsActive ?? true ) )
                {
                    var workflowService = new WorkflowService( rockContext );
                    var workflow = Rock.Model.Workflow.Activate( workflowType, string.Format("{0} {1}", firstName, lastName), rockContext );
                    workflowService.Add( workflow );
                    rockContext.SaveChanges();

                    workflow.SetAttributeValue( "FirstName", firstName);
                    workflow.SetAttributeValue( "LastName", lastName);
                    workflow.SetAttributeValue( "Email", email);
                    workflow.SaveAttributeValues();
                }
            }
        }
    }
}
