use log::{Level, Metadata, Record};

use crate::log_manager_provider;

pub static LOGGER: TelemetryCollectorLogBridge = TelemetryCollectorLogBridge;
pub static mut CONSOLE_ENABLED: bool = true;
pub static mut COLLECTOR_ENABLED: bool = true;

pub struct TelemetryCollectorLogBridge;

impl log::Log for TelemetryCollectorLogBridge {
    fn enabled(&self, metadata: &Metadata) -> bool {
        metadata.level() <= Level::Debug
    }

    fn log(&self, record: &Record) {
        if unsafe { CONSOLE_ENABLED } && self.enabled(record.metadata()) {
            let utc_now = chrono::Utc::now();
            let nanos = utc_now.timestamp_subsec_nanos();

            println!(
                "[{} {}.{}] {} <{}> - {}: {}",
                utc_now.date_naive().to_string(),
                utc_now.time().format("%H:%M:%S"),
                nanos,
                record.target(),
                record.module_path().unwrap(),
                record.level(),
                record.args()
            );
        }

        if unsafe { COLLECTOR_ENABLED }
            // Default
            && record.target() != "oneds_telemetry"
            // Items from deeper in the `oneds_telemetry` crate
            && !record.target().starts_with("oneds_telemetry::")
        {
            log_manager_provider().trace(format!("{}", record.args()).as_str());
        }
    }

    fn flush(&self) {
        if unsafe { COLLECTOR_ENABLED } {
            log_manager_provider().flush(false);
        }
    }
}

// fn map_severity_to_otel_severity(level: Level) -> Severity {
//     match level {
//         Level::Error => Severity::Error,
//         Level::Warn => Severity::Warn,
//         Level::Info => Severity::Info,
//         Level::Debug => Severity::Debug,
//         Level::Trace => Severity::Trace,
//     }
// }