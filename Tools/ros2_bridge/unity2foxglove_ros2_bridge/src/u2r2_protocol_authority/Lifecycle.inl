
PureSessionLifecycle::PureSessionLifecycle(const ProtocolLimits & limits)
: limits_(limits)
{
}

uint64_t PureSessionLifecycle::limit_for(TimeoutKind kind) const
{
  if (!limits_) {
    throw std::logic_error("this lifecycle has no limit snapshot");
  }
  switch (kind) {
    case TimeoutKind::handshake:
      return limits_->handshake_timeout_ms();
    case TimeoutKind::partial_frame:
      return limits_->partial_frame_timeout_ms();
    case TimeoutKind::read:
      return limits_->read_timeout_ms();
    case TimeoutKind::write:
      return limits_->write_timeout_ms();
    case TimeoutKind::join:
      return limits_->join_timeout_ms();
    case TimeoutKind::shutdown:
      return limits_->shutdown_timeout_ms();
    default:
      throw std::invalid_argument("unknown U2R2 timeout kind");
  }
}

bool PureSessionLifecycle::has_timed_out(
  TimeoutKind kind,
  uint64_t elapsed_ms) const
{
  return elapsed_ms >= limit_for(kind);
}

void PureSessionLifecycle::timeout(
  TimeoutKind kind,
  uint64_t elapsed_ms)
{
  if (!has_timed_out(kind, elapsed_ms)) {
    return;
  }
  state_ = PureSessionState::closed;
  throw ProtocolError(
          "timeout",
          "the U2R2 session exceeded its timeout",
          true);
}

void PureSessionLifecycle::peer_closed()
{
  state_ = PureSessionState::closed;
  throw ProtocolError(
          "peer_closed",
          "the U2R2 peer closed the session",
          true);
}

PureSessionState PureSessionLifecycle::state() const noexcept
{
  return state_;
}
