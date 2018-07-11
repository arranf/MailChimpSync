using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Data.Entity;
using System.Linq;
using Rock;
using Rock.Attribute;
using Rock.Data;
using Rock.Model;
using Rock.Web.Cache;
using Rock.Workflow;
using org.kcionline.MailchimpSync;

namespace org.kcionline.MailchimpSync.Workflow.Action
{
    [ActionCategory( "People" )]
    [Description( "Creates a mailchimp sync record for the selected person." )]
    [Export( typeof( ActionComponent ) )]
    [ExportMetadata( "ComponentName", "Person MailChimp Record Add" )]

    [WorkflowAttribute( "Person", "Workflow attribute that contains the person to create the record for.", true, "", "", 0, null, new string[] { "Rock.Field.Types.PersonFieldType" } )]
    [TextField( "List Id", "The id of the MailChimp list.", true, "", "", 2, LIST_ID_KEY )]
    [TextField( "API Key", "The API key used to interact with MailChimp.", true, "", "", 2, API_KEY_KEY )]
    public class CreateMailChimpRecord : ActionComponent
    {
        private const string LIST_ID_KEY = "listid";
        private const string API_KEY_KEY = "api";

        /// <summary>
        /// Executes the specified workflow.
        /// </summary>
        /// <param name="rockContext">The rock context.</param>
        /// <param name="action">The action.</param>
        /// <param name="entity">The entity.</param>
        /// <param name="errorMessages">The error messages.</param>
        /// <returns></returns>
        public override bool Execute( RockContext rockContext, WorkflowAction action, Object entity, out List<string> errorMessages )
        {
            errorMessages = new List<string>();

            var person = GetPersonAliasFromActionAttribute( "Person", rockContext, action, errorMessages );
            if (person == null)
            {
                action.AddLogEntry( "No person was provided.", true );
                return true;
            }

            string apiKey = GetActionAttributeValue( action, API_KEY_KEY );
            string listId = GetActionAttributeValue( action, LIST_ID_KEY );

            if (apiKey.IsNullOrWhiteSpace() || listId.IsNullOrWhiteSpace())
            {

                action.AddLogEntry( "No API key or no list id was provided.", true );
                return true;
            }

            try
            {
                Utils.AddOrUpdatePerson( person, listId, apiKey );
            }
            catch (Exception e)
            {
                errorMessages.Add( e.ToString() );
                foreach (string error in errorMessages)
                {
                    action.AddLogEntry( error );
                }
                return true;
            }


            return true;
        }

        private Person GetPersonAliasFromActionAttribute( string key, RockContext rockContext, WorkflowAction action, List<string> errorMessages )
        {
            string value = GetAttributeValue( action, key );
            Guid guidPersonAttribute = value.AsGuid();
            if ( !guidPersonAttribute.IsEmpty() )
            {
                var attributePerson = AttributeCache.Read( guidPersonAttribute, rockContext );
                if ( attributePerson != null )
                {
                    string attributePersonValue = action.GetWorklowAttributeValue( guidPersonAttribute );
                    if ( !string.IsNullOrWhiteSpace( attributePersonValue ) )
                    {
                        if ( attributePerson.FieldType.Class == "Rock.Field.Types.PersonFieldType" )
                        {
                            Guid personAliasGuid = attributePersonValue.AsGuid();
                            if ( !personAliasGuid.IsEmpty() )
                            {
                                PersonAliasService personAliasService = new PersonAliasService( rockContext );
                                return personAliasService.Queryable().AsNoTracking()
                                    .Where( a => a.Guid.Equals( personAliasGuid ) )
                                    .Select( a => a.Person )
                                    .FirstOrDefault();
                            }
                            else
                            {
                                errorMessages.Add( string.Format( "Person could not be found for selected value ('{0}')!", guidPersonAttribute.ToString() ) );
                                return null;
                            }
                        }
                        else
                        {
                            errorMessages.Add( string.Format( "The attribute used for {0} to provide the person was not of type 'Person'.", key ) );
                            return null;
                        }
                    }
                }
            }

            return null;
        }

    }
}
