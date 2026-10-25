using System.Text.Json;
using Atap.Core.Custom;

namespace Atap.Core.Tests.Custom;

/// <summary>Payloads in the shapes the services return, trimmed to the fields the tests use.</summary>
internal static class CustomFixtures
{
    /// <summary>The SPEC example: a home NAS status endpoint.</summary>
    public const string NasStatus = """
        {
          "host": "nas",
          "uptime": 1209600,
          "disk": { "usedPercent": 72.5, "usedGb": 2900.4, "totalGb": 4000 },
          "cpu": { "temp": 47.0 },
          "status": "healthy"
        }
        """;

    /// <summary>Pi-hole v6, GET /api/stats/summary.</summary>
    public const string PiHoleSummary = """
        {
          "queries": {
            "total": 48213,
            "blocked": 9734,
            "percent_blocked": 20.189575,
            "unique_domains": 3821,
            "forwarded": 27119,
            "cached": 11204,
            "frequency": 0.62,
            "types": {
              "A": 25012, "AAAA": 14988, "ANY": 0, "SRV": 12, "SOA": 3, "PTR": 401, "TXT": 77, "NAPTR": 0,
              "MX": 0, "DS": 0, "RRSIG": 0, "DNSKEY": 0, "NS": 1, "SVCB": 0, "HTTPS": 7719, "OTHER": 0
            }
          },
          "clients": { "active": 14, "total": 19 },
          "gravity": { "domains_being_blocked": 157420, "last_update": 1761350400 },
          "took": 0.00031
        }
        """;

    /// <summary>Pi-hole v5, GET /admin/api.php?summaryRaw.</summary>
    public const string PiHoleV5Summary = """
        {"domains_being_blocked":157420,"dns_queries_today":48213,"ads_blocked_today":9734,"ads_percentage_today":20.189575,"unique_domains":3821,"queries_forwarded":27119,"queries_cached":11204,"clients_ever_seen":19,"unique_clients":14,"privacy_level":0,"status":"enabled","gravity_last_updated":{"file_exists":true,"absolute":1761350400,"relative":{"days":0,"hours":9,"minutes":12}}}
        """;

    /// <summary>Uptime Kuma, GET /api/status-page/heartbeat/homelab. Status 0 is down, 1 up, 2 pending, 3 maintenance.</summary>
    public const string UptimeKumaHeartbeat = """
        {
          "heartbeatList": {
            "1": [
              { "status": 1, "time": "2026-10-25 01:58:03.112", "msg": "200 - OK", "ping": 23 },
              { "status": 1, "time": "2026-10-25 01:59:03.240", "msg": "200 - OK", "ping": 21 }
            ],
            "2": [
              { "status": 1, "time": "2026-10-25 01:58:10.008", "msg": "", "ping": 4 },
              { "status": 0, "time": "2026-10-25 01:59:10.019", "msg": "connect ECONNREFUSED 192.168.1.20:8096", "ping": null }
            ],
            "3": [
              { "status": 2, "time": "2026-10-25 01:59:40.551", "msg": "Pending", "ping": null }
            ]
          },
          "uptimeList": { "1_24": 1, "2_24": 0.9583, "3_24": 0.993 }
        }
        """;

    /// <summary>Uptime Kuma, GET /api/status-page/homelab.</summary>
    public const string UptimeKumaStatusPage = """
        {
          "config": { "slug": "homelab", "title": "Homelab", "description": null, "theme": "auto", "published": true },
          "incident": null,
          "publicGroupList": [
            {
              "id": 1, "name": "Services", "weight": 1,
              "monitorList": [
                { "id": 1, "name": "Jellyfin", "sendUrl": 0, "type": "http" },
                { "id": 2, "name": "Nextcloud", "sendUrl": 0, "type": "http" }
              ]
            },
            {
              "id": 2, "name": "Network", "weight": 2,
              "monitorList": [
                { "id": 3, "name": "Router", "sendUrl": 0, "type": "ping" }
              ]
            }
          ],
          "maintenanceList": []
        }
        """;

    /// <summary>Docker Engine API, GET /containers/json?all=true.</summary>
    public const string DockerContainers = """
        [
          { "Id": "8dfafdbc3a40", "Names": ["/jellyfin"], "Image": "jellyfin/jellyfin:10.10", "State": "running", "Status": "Up 3 days (healthy)" },
          { "Id": "9cd87474be90", "Names": ["/nextcloud"], "Image": "nextcloud:30-apache", "State": "running", "Status": "Up 3 days" },
          { "Id": "3176a2479c92", "Names": ["/backup"], "Image": "restic/restic:0.17", "State": "exited", "Status": "Exited (1) 2 hours ago" }
        ]
        """;

    /// <summary>Glances, GET /api/4/fs.</summary>
    public const string GlancesFileSystems = """
        [
          { "device_name": "/dev/nvme0n1p2", "fs_type": "ext4", "mnt_point": "/", "size": 502468108288, "used": 123649261568, "free": 353186766848, "percent": 25.9, "key": "mnt_point" },
          { "device_name": "/dev/md0", "fs_type": "btrfs", "mnt_point": "/volume1", "size": 3985729650688, "used": 3186271846400, "free": 799457804288, "percent": 79.9, "key": "mnt_point" }
        ]
        """;

    /// <summary>Home Assistant MQTT state topic, homeassistant/sensor/nas_cpu_temperature/state.</summary>
    public const string HomeAssistantPlainState = "47.0";

    /// <summary>Zigbee2MQTT, zigbee2mqtt/living_room_sensor.</summary>
    public const string Zigbee2MqttSensor = """{"battery":97,"humidity":61.42,"linkquality":102,"temperature":27.31,"voltage":3005}""";

    /// <summary>Tasmota smart plug, tele/plug_desk/SENSOR.</summary>
    public const string TasmotaSensor = """
        {"Time":"2026-10-25T10:15:00","ENERGY":{"TotalStartTime":"2026-01-02T09:00:00","Total":12.345,"Yesterday":0.412,"Today":0.188,"Period":3,"Power":45,"ApparentPower":51,"ReactivePower":24,"Factor":0.88,"Voltage":239,"Current":0.213}}
        """;

    /// <summary>Home Assistant MQTT Statestream publishes attributes JSON-encoded, homeassistant/sensor/nas_cpu_temperature/unit_of_measurement.</summary>
    public const string HomeAssistantStatestreamUnit = "\"°C\"";

    public static JsonElement Json(string text)
    {
        var result = PayloadReader.Read(text, PayloadFormat.Json);
        Assert.Equal(PayloadStatus.Ok, result.Status);
        return result.Root;
    }

    public static ValueTemplate Template(ValueTemplateSpec spec)
    {
        Assert.True(ValueTemplate.TryCreate(spec, out var template, out var error), error?.ToString());
        return template;
    }
}
