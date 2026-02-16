using Retirebot.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Retirebot
{
    public class ManagementClient
    {
        private HttpClient _client;

        public ManagementClient(HttpClient client)
        {
            _client = client;
        }
    }
}
