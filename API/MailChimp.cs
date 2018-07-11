using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MailChimp.Net.Core;
using MailChimp.Net.Interfaces;
using MailChimp.Net.Models;
using org.kcionline.MailchimpSync.Model;
using Rock.Model;

namespace org.kcionline.MailchimpSync.API
{
    public class MailChimpApi
    {

        private IMailChimpManager _manager;
        private readonly string _apiKey;
        private readonly string _listId;
        
        public MailChimpApi(string apiKey, string listId)
        {
            _apiKey = apiKey;
            _listId = listId;
        }

        public async Task<IEnumerable<Member>> GetListMembers()
        {
            return await _manager.Members.GetAllAsync( _listId, null ).ConfigureAwait( false );
        }

        public async Task RemoveFromMailChimp( MailChimpPersonAlias mailChimpPersonAlias)
        {
            await _manager.Members.DeleteAsync( _listId, mailChimpPersonAlias.Email ).ConfigureAwait( false );
        }

        public async Task<IEnumerable<ListSegment>> GetSegments()
        {
            return await _manager.ListSegments.GetAllAsync( _listId, null ).ConfigureAwait( false );
        }

        public async Task<ListSegment> AddSegment( String segmentName, IEnumerable<Person> people )
        {
            var segment = new Segment();
            segment.Name = segmentName;
            segment.EmailAddresses = people.Select( p => p.Email );
            return await _manager.ListSegments.AddAsync( _listId, segment );
        }

        public async Task<ListSegment> UpdateSegment( String segmentName, int segmentId, IEnumerable<Person> people )
        {
            var segment = new Segment();
            segment.Name = segmentName;
            segment.EmailAddresses = people.Select( p => p.Email );
            return await _manager.ListSegments.UpdateAsync( _listId, segmentId.ToString(), segment );
        }

        public async Task DeleteSegment(int segmentId)
        {
            await _manager.ListSegments.DeleteAsync( _listId, segmentId.ToString() );
        }

        public async Task<Member> AddOrUpdateMailChimpMember( Member member )
        {
            return await _manager.Members.AddOrUpdateAsync( _listId, member ).ConfigureAwait( false );
        }
    }
}
