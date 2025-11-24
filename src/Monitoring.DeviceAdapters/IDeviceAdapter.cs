using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Monitoring.DeviceAdapters
{
    public enum ProtocolType
    {
        Unknown = 0,
        Serial,
        ModbusTcp,
        TcpSocket,
        OpcUa,
        File
    }

    public sealed record DeviceConnectionInfo(
        string DeviceId,
        ProtocolType ProtocolType,
        IReadOnlyDictionary<string, string> Parameters);

    public sealed record TagDefinition(
        string TagId,
        string Address,
        string DataType,
        int? PollingIntervalMs = null,
        int? TimeoutMs = null,
        int? RetryCount = null);

    public sealed record TagValue(
        string TagId,
        object? Value,
        DateTime TimestampUtc,
        string Quality);

    public sealed record AdapterCapabilities(
        bool SupportsBatchRead,
        bool SupportsWrite,
        bool SupportsSubscriptions,
        bool SupportsSimulationMode);

    public interface IDeviceAdapter : IAsyncDisposable
    {
        string AdapterId { get; }
        ProtocolType ProtocolType { get; }
        AdapterCapabilities Capabilities { get; }
        bool IsConnected { get; }

        /// <summary>
        /// 초기화 단계에서 호출되며, 장치 연결 정보와 태그 정의를 받아 내부 리소스를 준비한다.
        /// </summary>
        Task InitializeAsync(DeviceConnectionInfo connectionInfo, IReadOnlyCollection<TagDefinition> tags, CancellationToken cancellationToken = default);

        /// <summary>
        /// 장치와의 연결을 수립한다. 재연결 시도 정책은 구현체가 책임진다.
        /// </summary>
        Task ConnectAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 장치와의 연결을 종료하고 자원을 해제한다.
        /// </summary>
        Task DisconnectAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 단일 태그를 읽는다. 폴링 워커가 태그별 주기에 맞춰 호출한다.
        /// </summary>
        Task<TagValue> ReadTagAsync(TagDefinition tag, CancellationToken cancellationToken = default);

        /// <summary>
        /// 복수 태그를 일괄 읽는다. 어댑터가 지원하지 않으면 각 태그를 개별 호출로 처리한다.
        /// </summary>
        Task<IReadOnlyCollection<TagValue>> ReadTagsAsync(IReadOnlyCollection<TagDefinition> tags, CancellationToken cancellationToken = default);

        /// <summary>
        /// 제어 명령을 태그 단위로 수행한다. 반환값은 장치가 회신한 값 또는 Ack를 포함한다.
        /// </summary>
        Task<TagValue> WriteTagAsync(TagDefinition tag, object? value, CancellationToken cancellationToken = default);

        /// <summary>
        /// 이벤트/스트리밍 기반 데이터 수신을 구독한다. 미지원 어댑터는 NotSupportedException을 던질 수 있다.
        /// 반환된 IAsyncEnumerable은 연결이 유지되는 동안 값을 방출한다.
        /// </summary>
        IAsyncEnumerable<TagValue> SubscribeAsync(IReadOnlyCollection<TagDefinition> tags, CancellationToken cancellationToken = default);
    }
}
