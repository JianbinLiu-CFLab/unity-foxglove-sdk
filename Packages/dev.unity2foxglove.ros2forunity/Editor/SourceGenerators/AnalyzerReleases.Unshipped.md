; Unshipped analyzer releases for the R2FU FoxRun Provider.

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
FOXR2F001 | FoxRun.R2FU | Error | R2FU route selection is invalid.
FOXR2F002 | FoxRun.R2FU | Error | Packaged ROS messages must implement the ros2cs message contract.
FOXR2F003 | FoxRun.R2FU | Error | Packaged ROS messages require a public parameterless constructor.
FOXR2F004 | FoxRun.R2FU | Error | Packaged ROS messages require a canonical package.msg namespace.
FOXR2F005 | FoxRun.R2FU | Error | An explicit schema name does not match the validated ROS message identity.
FOXR2F006 | FoxRun.R2FU | Error | The packaged ROS message copy shape is unsupported.
FOXR2F007 | FoxRun.R2FU | Error | R2FU generation requires the optional native assembly reference.
FOXR2F008 | FoxRun.R2FU | Error | R2FU full duplex requires a complete supported custom DTO contract.
FOXR2F009 | FoxRun.R2FU | Error | The custom ROS DTO shape is unsupported.
FOXR2F010 | FoxRun.R2FU | Error | Custom ROS DTO values require a public parameterless constructor.
FOXR2F011 | FoxRun.R2FU | Error | Custom ROS DTO inbound members must be readable and writable.
FOXR2F012 | FoxRun.R2FU | Error | The R2FU publish route selection is invalid.
FOXR2F013 | FoxRun.R2FU | Error | The R2FU route is invalid for the declared direction.
FOXR2F014 | FoxRun.R2FU | Error | The R2FU QoS contract is invalid.
FOXR2F015 | FoxRun.R2FU | Error | R2FU QoS requires an R2FU direction.
FOXR2F016 | FoxRun.R2FU | Error | Same-topic R2FU members have incompatible directional QoS.
