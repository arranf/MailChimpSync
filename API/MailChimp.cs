using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MailChimp.Net;
using MailChimp.Net.Core;
using MailChimp.Net.Interfaces;
using MailChimp.Net.Models;
using org.kcionline.MailchimpSync.Model;
using Rock.Model;

namespace org.kcionline.MailchimpSync.API
{
    public class MailChimpApi
    {
        public IMailChimpManager Manager { get; set; }
 
        public string ListId { get; }

        public MailChimpApi(string apiKey, string listId)
        {
            ListId = listId;
            Manager = new MailChimpManager( apiKey );
        }

        public async Task<IEnumerable<Member>> GetListMembers()
        {
            return await Manager.Members.GetAllAsync( ListId, null ).ConfigureAwait( false );
        }

        public async Task RemoveFromMailChimp( MailChimpPersonAlias mailChimpPersonAlias)
        {
            await Manager.Members.DeleteAsync( ListId, mailChimpPersonAlias.Email ).ConfigureAwait( false );
        }

        public async Task<IEnumerable<ListSegment>> GetSegments()
        {
            return await Manager.ListSegments.GetAllAsync( ListId, null ).ConfigureAwait( false );
        }

        public async Task<ListSegment> AddSegment( String segmentName, IEnumerable<Person> people )
        {
            var segment = new Segment();
            segment.Name = segmentName;
            segment.EmailAddresses = people.Select( p => p.Email );
            return await Manager.ListSegments.AddAsync( ListId, segment );
        }

        public async Task<ListSegment> UpdateSegment( String segmentName, int segmentId, IEnumerable<Person> people )
        {
            var segment = new Segment();
            segment.Name = segmentName;
            segment.EmailAddresses = people.Select( p => p.Email );
            return await Manager.ListSegments.UpdateAsync( ListId, segmentId.ToString(), segment );
        }

        public async Task DeleteSegment(int segmentId)
        {
            await Manager.ListSegments.DeleteAsync( ListId, segmentId.ToString() );
        }

        public async Task<Member> AddOrUpdateMailChimpMember( Member member )
        {
            return await Manager.Members.AddOrUpdateAsync( ListId, member ).ConfigureAwait( false );
        }
    }
}
