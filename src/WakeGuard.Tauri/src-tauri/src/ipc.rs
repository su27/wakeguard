use chrono::{DateTime, Utc};
use serde::{Deserialize, Serialize};
use tokio::{
    io::{AsyncReadExt, AsyncWriteExt},
    net::windows::named_pipe::ClientOptions,
};

pub const MAX_MESSAGE: usize = 16 * 1024;
const PIPE: &str = r"\\.\pipe\WakeGuard.Service.v1";

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub enum Mode {
    Inactive,
    KeepAwake,
    KeepAwakeAndDisplayOn,
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct Request<'a> {
    pub protocol_version: i32,
    pub kind: &'a str,
    pub client_id: &'a str,
    pub session_id: u32,
    pub process_id: u32,
    pub requested_mode: Mode,
    pub stop_at_utc: Option<DateTime<Utc>>,
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Response {
    pub protocol_version: i32,
    pub success: bool,
    pub error_code: Option<String>,
    pub error_message: Option<String>,
    pub effective_mode: Mode,
    pub client_mode: Mode,
}

pub fn encode(request: &Request<'_>) -> Result<Vec<u8>, String> {
    let bytes = serde_json::to_vec(request).map_err(|e| e.to_string())?;
    if bytes.is_empty() || bytes.len() > MAX_MESSAGE {
        return Err("Invalid IPC request length".into());
    }
    let mut frame = Vec::with_capacity(bytes.len() + 4);
    frame.extend_from_slice(&(bytes.len() as u32).to_le_bytes());
    frame.extend_from_slice(&bytes);
    Ok(frame)
}

async fn once(frame: &[u8]) -> Result<Response, String> {
    let mut pipe = ClientOptions::new().open(PIPE).map_err(|e| e.to_string())?;
    pipe.write_all(frame).await.map_err(|e| e.to_string())?;
    let size = pipe.read_u32_le().await.map_err(|e| e.to_string())? as usize;
    if size == 0 || size > MAX_MESSAGE {
        return Err("Invalid IPC response length".into());
    }
    let mut bytes = vec![0; size];
    pipe.read_exact(&mut bytes)
        .await
        .map_err(|e| e.to_string())?;
    let response: Response = serde_json::from_slice(&bytes).map_err(|e| e.to_string())?;
    if response.protocol_version != 1 {
        return Err("Incompatible service protocol".into());
    }
    Ok(response)
}

pub async fn send(request: &Request<'_>) -> Result<Response, String> {
    let frame = encode(request)?;
    let mut last_error = "Service connection timed out".to_string();
    let result = tokio::time::timeout(std::time::Duration::from_secs(5), async {
        for attempt in 0.. {
            match once(&frame).await {
                Ok(response) if response.success => {
                    if request.kind != "QueryStatus"
                        && response.client_mode != request.requested_mode
                    {
                        return Err("Service did not confirm the requested mode".into());
                    }
                    return Ok(response);
                }
                Ok(response) if response.error_code.as_deref() != Some("service_stopping") => {
                    return Err(response
                        .error_message
                        .or(response.error_code)
                        .unwrap_or_else(|| "Service operation failed".into()))
                }
                Ok(_) => last_error = "Service is restarting".into(),
                Err(error) => last_error = error,
            }
            tokio::time::sleep(std::time::Duration::from_millis(
                (100 * (attempt + 1)).min(500),
            ))
            .await;
        }
        unreachable!()
    })
    .await;
    result.unwrap_or_else(|_| Err(last_error))
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn frame_matches_dotnet_camel_case_and_string_enums() {
        let frame = encode(&Request {
            protocol_version: 1,
            kind: "UpsertLease",
            client_id: "test",
            session_id: 2,
            process_id: 3,
            requested_mode: Mode::KeepAwakeAndDisplayOn,
            stop_at_utc: None,
        })
        .unwrap();
        assert_eq!(
            u32::from_le_bytes(frame[..4].try_into().unwrap()) as usize,
            frame.len() - 4
        );
        let value: serde_json::Value = serde_json::from_slice(&frame[4..]).unwrap();
        assert_eq!(value["requestedMode"], "KeepAwakeAndDisplayOn");
        assert_eq!(value["kind"], "UpsertLease");
        assert_eq!(value["protocolVersion"], 1);
        assert!(value["stopAtUtc"].is_null());
    }
    #[test]
    fn reads_service_response_with_additional_fields() {
        let response: Response = serde_json::from_str(r#"{"protocolVersion":1,"success":true,"effectiveMode":"KeepAwake","clientMode":"Inactive","serverTimeUtc":"2026-10-14T00:00:00Z","activeLeaseCount":1}"#).unwrap();
        assert_eq!(response.effective_mode, Mode::KeepAwake);
        assert_eq!(response.client_mode, Mode::Inactive);
    }

    #[tokio::test]
    #[ignore = "Requires the installed WakeGuard service; creates and releases an isolated test lease"]
    async fn installed_service_round_trip() {
        let client = uuid::Uuid::new_v4().to_string();
        let mut request = Request {
            protocol_version: 1,
            kind: "QueryStatus",
            client_id: &client,
            session_id: crate::native::session_id(),
            process_id: std::process::id(),
            requested_mode: Mode::Inactive,
            stop_at_utc: None,
        };
        assert_eq!(send(&request).await.unwrap().client_mode, Mode::Inactive);
        request.kind = "UpsertLease";
        request.requested_mode = Mode::KeepAwake;
        request.stop_at_utc = Some(Utc::now() + chrono::Duration::seconds(5));
        let result = send(&request).await;
        request.kind = "ReleaseLease";
        request.requested_mode = Mode::Inactive;
        request.stop_at_utc = None;
        let released = send(&request).await;
        assert_eq!(result.unwrap().client_mode, Mode::KeepAwake);
        assert_eq!(released.unwrap().client_mode, Mode::Inactive);
    }
}
