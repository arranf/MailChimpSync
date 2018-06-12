<%@ WebHandler Language="C#" Class="MailChimp" %>

using System;
using System.Data.Entity;
using System.Web;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;

using Newtonsoft.Json;

using org.kcionline.MailchimpSync.Model;

using Rock;
using Rock.Data;
using Rock.Model;
using Rock.Web.Cache;

public class MailChimp : IHttpHandler
{
    private HttpRequest request;
    private HttpResponse response;

    public void ProcessRequest( HttpContext context )
    {
        request = context.Request;
        response = context.Response;

        response.ContentType = "text/plain";

        if ( request.HttpMethod != "POST" && request.HttpMethod != "GET" )
        {
            response.Write( "Invalid request type." );
            response.StatusCode = 406;
            return;
        }

        using ( var rockContext = new Rock.Data.RockContext() )
        {

            string type = request.Form["type"];
            string action = request.Form["data[action]"];
            string reason = request.Form["data[reason]"];
            string timestamp = request.Form["fired_at"];
            string listId = request.Form["data[list_id]"];
            int personAliasId = request.Form["data[merges][PERSONALIA]"].AsInteger();

            if (type == "unsubscribe")
            {
                var mailChimpAliasService = new MailChimpPersonAliasService( rockContext );
                var mailChimpAliasPerson = mailChimpAliasService.GetByPersonAliasId(personAliasId);
                if (mailChimpAliasPerson != null)
                {
                    mailChimpAliasService.Delete( mailChimpAliasPerson );
                }

                var syncEntityType = EntityTypeCache.Read( "org.kcionline.MailchimpSync.Model.MailChimpPersonAlias", false );
                if ( syncEntityType != null )
                {
                    var attributeService = new AttributeValueService( rockContext );
                    var validEntityIds = attributeService
                         .Queryable()
                         .AsNoTracking()
                         .Where( v => v.Attribute.EntityTypeQualifierValue == "org.kcionline.MailchimpSync.Jobs.Sync" )
                         .GroupBy( a => a.EntityId )
                         .Where( a => a.Select( b => b.Value ).Any( v => v == listId) )
                         .Select( k => k.Key);

                    var groupTypeGuids = attributeService.Queryable().AsNoTracking().Where( av => av.Attribute.Key == "GroupType" && validEntityIds.Contains( av.EntityId )).ToList().Select(av => av.Value.AsGuid());
                    var personId = new PersonAliasService( rockContext ).Get( personAliasId ).PersonId;
                    var groupMemberService = new GroupMemberService( rockContext );
                    var groupMembers = groupMemberService.GetByPersonId( personId ).Where( gm => groupTypeGuids.Contains( gm.Group.GroupType.Guid ) );
                    if (groupMembers != null && groupMembers.Count() > 0)
                    {
                        groupMemberService.DeleteRange( groupMembers );
                    }
                    rockContext.SaveChanges();
                } else
                {

                    throw new Exception( "No sync job set" );
                }

            }

            if ( !ValidSignature( request.QueryString.Get("secret") ) )
            {
                response.Write( "Invalid request signature." );
                response.StatusCode = 406;
                return;
            }

            response.Write( String.Format( "Successfully processed") );
            response.StatusCode = 200;
        }
    }

    private static bool ValidSignature( string secret)
    {
        var value = GlobalAttributesCache.Value( "org_kcionline_MailChimpSync_Secret" );
        return value == secret;
    }

    public bool IsReusable
    {
        get
        {
            return false;
        }
    }
}