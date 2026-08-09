using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using LiteNetLib;
using TopSpeed.Localization;
using TopSpeed.Protocol;
using TopSpeed.Server.Logging;

namespace TopSpeed.Server.Network
{
    internal sealed class UdpServerTransport : IDisposable
    {
        private const int DefaultUpdateTimeMs = 1;
        private const int DefaultPingIntervalMs = 1000;
        private const int DisconnectTimeoutPaddingMs = 15000;

        private readonly Logger _logger;
        private readonly object _peerLock = new object();
        private EventBasedNetListener? _listener;
        private NetManager? _server;
        private readonly Dictionary<string, NetPeer> _peers = new Dictionary<string, NetPeer>(StringComparer.OrdinalIgnoreCase);

        public event Action<IPEndPoint, byte[]>? PacketReceived;
        public event Action<IPEndPoint, TransportDisconnectClassification, DisconnectReason, SocketError>? PeerDisconnected;
        public event Action<IPEndPoint, SocketError>? NetworkError;
        public event Action<IPEndPoint, int>? PeerLatencyUpdated;
        public event Action<IPEndPoint, IPEndPoint>? PeerAddressChanged;

        public UdpServerTransport(Logger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public void Start(int port)
        {
            if (_server != null)
                return;

            _listener = new EventBasedNetListener();
            _listener.ConnectionRequestEvent += request => request.AcceptIfKey(ProtocolConstants.ConnectionKey);
            _listener.PeerConnectedEvent += peer =>
            {
                lock (_peerLock)
                    _peers[GetPeerKey(CreatePeerEndpoint(peer))] = peer;
            };
            _listener.PeerAddressChangedEvent += (peer, previousAddress) =>
            {
                if (previousAddress == null)
                    return;

                var oldKey = previousAddress.ToString();
                var newEndPoint = CreatePeerEndpoint(peer);
                lock (_peerLock)
                {
                    _peers.Remove(oldKey);
                    _peers[GetPeerKey(newEndPoint)] = peer;
                }

                PeerAddressChanged?.Invoke(previousAddress, newEndPoint);
            };
            _listener.PeerDisconnectedEvent += (peer, disconnectInfo) =>
            {
                var endpoint = CreatePeerEndpoint(peer);
                lock (_peerLock)
                    _peers.Remove(GetPeerKey(endpoint));

                var classification = DisconnectMapping.FromTransportReason(disconnectInfo.Reason);
                PeerDisconnected?.Invoke(endpoint, classification, disconnectInfo.Reason, disconnectInfo.SocketErrorCode);
            };
            _listener.NetworkErrorEvent += (endPoint, socketError) =>
            {
                var endpoint = endPoint ?? new IPEndPoint(IPAddress.None, 0);
                NetworkError?.Invoke(endpoint, socketError);
            };
            _listener.NetworkLatencyUpdateEvent += (peer, latency) =>
            {
                PeerLatencyUpdated?.Invoke(CreatePeerEndpoint(peer), latency);
            };
            _listener.NetworkReceiveEvent += (peer, reader, _, _) =>
            {
                try
                {
                    var buffer = reader.GetRemainingBytes();
                    if (buffer.Length == 0)
                        return;

                    PacketReceived?.Invoke(CreatePeerEndpoint(peer), buffer);
                }
                finally
                {
                    reader.Recycle();
                }
            };

            var disconnectTimeoutMs = Math.Max(
                (int)ConnectionRecoveryRules.DefaultHeartbeatMissWindow.TotalMilliseconds + DisconnectTimeoutPaddingMs,
                45000);

            _server = new NetManager(_listener)
            {
                ReuseAddress = true,
                UpdateTime = DefaultUpdateTimeMs,
                PingInterval = DefaultPingIntervalMs,
                DisconnectTimeout = disconnectTimeoutMs,
                DisconnectOnUnreachable = false,
                AllowPeerAddressChange = true,
                UnsyncedEvents = false,
                UnsyncedReceiveEvent = false,
                UnsyncedDeliveryEvent = false,
                AutoRecycle = false,
                EnableStatistics = true,
                UseNativeSockets = false,
                MtuOverride = 0,
                MtuDiscovery = false,
                ChannelsCount = PacketStreams.Count
            };

            if (!_server.Start(port))
                throw new InvalidOperationException(LocalizationService.Format(
                    LocalizationService.Mark("Failed to start transport on port {0}."),
                    port));

            _logger.Info(LocalizationService.Format(LocalizationService.Mark("LiteNetLib transport listening on {0}."), port));
        }

        public void Pump()
        {
            try
            {
                _server?.PollEvents();
            }
            catch (Exception ex)
            {
                _logger.Warning(LocalizationService.Format(
                    LocalizationService.Mark("LiteNetLib poll failed: {0}"),
                    ex.Message));
            }
        }

        public void Stop()
        {
            var server = _server;
            _server = null;

            // An orderly shutdown stops the transport and then disposes it, so this is reached
            // twice every time. Only the first has anything to do, and only the first is worth
            // reporting: a second "transport stopped" in the log reads as a second transport.
            if (server == null)
                return;

            try
            {
                server?.Stop(sendDisconnectMessages: false);
            }
            catch (Exception ex)
            {
                _logger.Warning(LocalizationService.Format(
                    LocalizationService.Mark("LiteNetLib stop failed: {0}"),
                    ex.Message));
            }

            lock (_peerLock)
                _peers.Clear();
            _listener = null;
            _logger.Info(LocalizationService.Mark("LiteNetLib transport stopped."));
        }

        public void Send(IPEndPoint endPoint, byte[] payload, DeliveryMethod deliveryMethod = DeliveryMethod.ReliableOrdered)
        {
            Send(endPoint, payload, deliveryMethod, channel: 0);
        }

        public void Send(IPEndPoint endPoint, byte[] payload, DeliveryMethod deliveryMethod, byte channel)
        {
            if (_server == null || payload == null || payload.Length == 0)
                return;

            NetPeer? peer;
            lock (_peerLock)
                _peers.TryGetValue(endPoint.ToString(), out peer);

            if (peer == null || peer.ConnectionState != ConnectionState.Connected)
                return;

            try
            {
                peer.Send(payload, channel, deliveryMethod);
            }
            catch (Exception ex)
            {
                _logger.Warning(LocalizationService.Format(
                    LocalizationService.Mark("LiteNetLib send failed: {0}"),
                    ex.Message));
            }
        }

        private static string GetPeerKey(IPEndPoint endPoint)
        {
            return endPoint.ToString();
        }

        private static IPEndPoint CreatePeerEndpoint(NetPeer peer)
        {
            return new IPEndPoint(peer.Address, peer.Port);
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
