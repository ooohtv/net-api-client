/*
 This file is auto-generated, do not edit
*/


using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Recombee.ApiClient.ApiRequests;
using Recombee.ApiClient.Bindings;

namespace Recombee.ApiClient.Tests
{
    public class UserBasedRecommendationBatchUnitTest: RecommendationUnitTest
    {

        [Fact]
        public void TestUserBasedRecommendation()
        {
            Request[] requests = new Request[] {
                new UserBasedRecommendation("entity_id",9),
                new UserBasedRecommendation("nonexisting",9,cascadeCreate: true),
                new UserBasedRecommendation("nonexisting2",9,cascadeCreate: true,expertSettings: new Dictionary<string, object>(){})
            };

            BatchResponse batchResponse = client.Send(new Batch(requests));
            Assert.Equal(200, (int)batchResponse.StatusCodes.ElementAt(0));
            Assert.Equal(9, ((IEnumerable<Recommendation>) batchResponse[0]).Count());
            Assert.Equal(200, (int)batchResponse.StatusCodes.ElementAt(1));
            Assert.Equal(9, ((IEnumerable<Recommendation>) batchResponse[1]).Count());
            Assert.Equal(200, (int)batchResponse.StatusCodes.ElementAt(2));
            Assert.Equal(9, ((IEnumerable<Recommendation>) batchResponse[2]).Count());
        }
    }
}
