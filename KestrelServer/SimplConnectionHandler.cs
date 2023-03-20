using System.Threading.Tasks;
using Microsoft.AspNetCore.Connections;
using SimplPipelines;

namespace KestrelServer
{
    public class SimplConnectionHandler : ConnectionHandler
    {
        private readonly SimplPipelineServer _server;
        public SimplConnectionHandler(SimplPipelineServer server) => _server = server;
        public override async Task OnConnectedAsync(ConnectionContext connection)
        {
            await _server.RunClientAsync(connection.Transport);
            // TODO LRB
            // catch (IOException io) when (io.InnerException is UvException uv && uv.StatusCode == -4077)
        }
    }
}
