namespace bridge_runtime = unity2foxglove::ros2_bridge::runtime;
namespace u2r2 = unity2foxglove::ros2_bridge::u2r2;

// U2R2 wire limits shared with the Unity frame writer. Keep these constants
// in sync before changing the binary frame envelope.
constexpr uint16_t kVersion = 1;
constexpr uint16_t kFlags = 0;
constexpr uint32_t kMaxHeaderBytes = 64U * 1024U;
constexpr uint32_t kMaxPayloadBytes = 64U * 1024U * 1024U;
constexpr uint8_t kCdrLittleEndianHeader[4] = {0x00, 0x01, 0x00, 0x00};
constexpr auto kReadStallTimeout = std::chrono::seconds(5);
constexpr int kHealthProtocolVersion = 1;
constexpr int kPublisherPreparationProtocolVersion = 1;
constexpr const char * kSidecarName = "unity2foxglove_ros2_bridge";
constexpr const char * kSidecarVersion = "0.1.0";
constexpr uint64_t kControlResponseReservationBytes = 4096;
constexpr size_t kMaximumResponseErrorBytes = 512;

#ifdef _WIN32
using SocketHandle = SOCKET;
using SocketLength = int;
constexpr SocketHandle kInvalidSocket = INVALID_SOCKET;
#else
using SocketHandle = int;
using SocketLength = socklen_t;
constexpr SocketHandle kInvalidSocket = -1;
#endif

int last_socket_error()
{
#ifdef _WIN32
  return WSAGetLastError();
#else
  return errno;
#endif
}

bool socket_error_is_interrupted(int error)
{
#ifdef _WIN32
  return error == WSAEINTR;
#else
  return error == EINTR;
#endif
}

bool socket_error_is_retryable_timeout(int error)
{
#ifdef _WIN32
  return error == WSAEWOULDBLOCK || error == WSAETIMEDOUT;
#else
  return error == EAGAIN || error == EWOULDBLOCK;
#endif
}

std::string socket_error_text(int error)
{
#ifdef _WIN32
  return "WinSock error " + std::to_string(error);
#else
  return std::strerror(error);
#endif
}

void close_socket(SocketHandle socket)
{
  if (socket == kInvalidSocket) {
    return;
  }
#ifdef _WIN32
  ::closesocket(socket);
#else
  ::close(socket);
#endif
}

void shutdown_socket_both(SocketHandle socket)
{
  if (socket == kInvalidSocket) {
    return;
  }
#ifdef _WIN32
  (void)::shutdown(socket, SD_BOTH);
#else
  (void)::shutdown(socket, SHUT_RDWR);
#endif
}

int socket_select_width(SocketHandle socket)
{
#ifdef _WIN32
  (void)socket;
  return 0;
#else
  return socket + 1;
#endif
}

int set_socket_option(
  SocketHandle socket,
  int level,
  int option,
  const void * value,
  SocketLength length)
{
#ifdef _WIN32
  return ::setsockopt(
    socket,
    level,
    option,
    reinterpret_cast<const char *>(value),
    length);
#else
  return ::setsockopt(socket, level, option, value, length);
#endif
}

std::ptrdiff_t receive_socket(SocketHandle socket, uint8_t * data, size_t size)
{
  const auto bounded = static_cast<int>(
    std::min(size, static_cast<size_t>(std::numeric_limits<int>::max())));
#ifdef _WIN32
  return static_cast<std::ptrdiff_t>(
    ::recv(socket, reinterpret_cast<char *>(data), bounded, 0));
#else
  return static_cast<std::ptrdiff_t>(::recv(socket, data, static_cast<size_t>(bounded), 0));
#endif
}

std::ptrdiff_t send_socket(SocketHandle socket, const uint8_t * data, size_t size)
{
  const auto bounded = static_cast<int>(
    std::min(size, static_cast<size_t>(std::numeric_limits<int>::max())));
#ifdef _WIN32
  return static_cast<std::ptrdiff_t>(
    ::send(socket, reinterpret_cast<const char *>(data), bounded, 0));
#else
#ifdef MSG_NOSIGNAL
  return static_cast<std::ptrdiff_t>(::send(
    socket, data, static_cast<size_t>(bounded), MSG_NOSIGNAL));
#else
  return static_cast<std::ptrdiff_t>(::send(socket, data, static_cast<size_t>(bounded), 0));
#endif
#endif
}

void configure_client_timeouts(SocketHandle socket)
{
#ifdef _WIN32
  const DWORD timeout_ms = 250;
  set_socket_option(
    socket,
    SOL_SOCKET,
    SO_RCVTIMEO,
    &timeout_ms,
    static_cast<SocketLength>(sizeof(timeout_ms)));
  set_socket_option(
    socket,
    SOL_SOCKET,
    SO_SNDTIMEO,
    &timeout_ms,
    static_cast<SocketLength>(sizeof(timeout_ms)));
#else
  timeval timeout {};
  timeout.tv_sec = 0;
  timeout.tv_usec = 250000;
  set_socket_option(
    socket,
    SOL_SOCKET,
    SO_RCVTIMEO,
    &timeout,
    static_cast<SocketLength>(sizeof(timeout)));
  set_socket_option(
    socket,
    SOL_SOCKET,
    SO_SNDTIMEO,
    &timeout,
    static_cast<SocketLength>(sizeof(timeout)));
#endif
}

#ifdef _WIN32
class WinsockRuntime
{
public:
  WinsockRuntime()
  {
    WSADATA data {};
    const auto result = WSAStartup(MAKEWORD(2, 2), &data);
    if (result != 0) {
      throw std::runtime_error("WSAStartup failed: " + socket_error_text(result));
    }
    initialized_ = true;
  }

  ~WinsockRuntime()
  {
    if (initialized_) {
      WSACleanup();
    }
  }

  WinsockRuntime(const WinsockRuntime &) = delete;
  WinsockRuntime & operator=(const WinsockRuntime &) = delete;

private:
  bool initialized_ = false;
};
#endif

enum class PayloadFormat
{
  CdrWithEncapsulation,
  CdrBodyOnly
};
