// Copyright 2021 - Haga Rakotoharivelo - https://github.com/haga-rak

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Fluxzy.Certificates;
using Fluxzy.Core;
using Fluxzy.Extensions;
using Fluxzy.Writers;

namespace Fluxzy.Tests._Fixtures
{
    public class AddHocConfigurableProxy : IAsyncDisposable
    {
        private readonly CancellationTokenSource _cancellationSource;
        private readonly List<Exchange> _capturedExchanges = new();
        private readonly TaskCompletionSource _completionSource;
        private readonly int _expectedRequestCount;

        private int _requestCount;

        public AddHocConfigurableProxy(
            int expectedRequestCount = 1, int timeoutSeconds = 5,
            Action<FluxzySetting> ? configureSetting = null, 
            ITcpConnectionProvider ? connectionProvider = null)
        {
            _expectedRequestCount = expectedRequestCount;

            BindHost = "127.0.0.1";

            StartupSetting = FluxzySetting
                             .CreateDefault()
                             .SetBoundAddress(BindHost, 0);

            configureSetting?.Invoke(StartupSetting);

            InternalProxy = new Proxy(StartupSetting,
                new CertificateProvider(StartupSetting.CaCertificate, new InMemoryCertificateCache()),
                new DefaultCertificateAuthorityManager(), userAgentProvider: new UaParserUserAgentInfoProvider(),
                tcpConnectionProvider: connectionProvider);

            InternalProxy.Writer.ExchangeUpdated += ProxyOnBeforeResponse;

            _cancellationSource = new CancellationTokenSource(timeoutSeconds * 1000);
            _completionSource = new TaskCompletionSource();

            _cancellationSource.Token.Register(() => {
                if (!_completionSource.Task.IsCompleted) {
                    _completionSource.TrySetException(
                        new TimeoutException($"Timeout of {timeoutSeconds} seconds reached"));
                }
            });
        }

        public Proxy InternalProxy { get; }
        
        public string BindHost { get; }

        public ImmutableList<Exchange> CapturedExchanges {
            get
            {
                lock (_capturedExchanges) {
                    return _capturedExchanges.ToImmutableList();
                }
            }
        }

        public FluxzySetting StartupSetting { get; }

        public async ValueTask DisposeAsync()
        {
            await InternalProxy.DisposeAsync();
            _cancellationSource.Dispose();
        }

        public IReadOnlyCollection<IPEndPoint> Run()
        {
            return InternalProxy.Run();
        }

        public HttpClient RunAndGetClient()
        {
            var endPoint = Run().First();
            var clientHandler = new HttpClientHandler();
            clientHandler.Proxy = new WebProxy($"http://{endPoint}");

            return new HttpClient(clientHandler);
        }


        private void ProxyOnBeforeResponse(object? sender, ExchangeUpdateEventArgs exchangeUpdateEventArgs)
        {
            if (exchangeUpdateEventArgs.UpdateType != ArchiveUpdateType.AfterResponseHeader)
                return;

            lock (_capturedExchanges) {
                _capturedExchanges.Add(exchangeUpdateEventArgs.Original);
            }

            if (Interlocked.Increment(ref _requestCount) >= _expectedRequestCount)
                _completionSource.TrySetResult();
        }

        public Task WaitUntilDone()
        {
            return _completionSource.Task;
        }
    }
}
