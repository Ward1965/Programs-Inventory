use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct Program {
    pub id: String,
    pub name: String,
    pub publisher: Option<String>,
    pub version: Option<String>,
    pub install_location: Option<String>,
    pub display_icon: Option<String>,
    pub install_date: Option<String>,
    pub architecture: Option<String>,
    pub source: String,
    pub status: String,
    pub has_icon: bool,
}