# Synapse Admin API Documentation (April 2026)

This document provides a comprehensive and formatted overview of the Synapse Admin API for managing a Matrix homeserver.

---

## 🔐 Authentication & Access

### Authenticate as a server admin
Many of the API calls in the admin api will require an `access_token` for a server admin. (Note that a server admin is distinct from a room admin.)

#### Elevating an existing user
An existing user can be marked as a server admin by updating the database directly. 
Check your database settings in the configuration file, connect to the correct database using either `psql [database name]` (if using PostgreSQL) or `sqlite3 path/to/your/database.db` (if using SQLite) and elevate the user `@foo:bar.com` to administrator.

```sql
UPDATE users SET admin = 1 WHERE name = '@foo:bar.com';
```

#### Creating a new server admin
A new server admin user can also be created using the `register_new_matrix_user` command. This is a script that is distributed as part of synapse. It is possibly already on your `$PATH` depending on how Synapse was installed.

#### Finding the access token
Finding your user’s `access_token` is client-dependent, but will usually be shown in the client’s settings.

---

### Making an Admin API Request
For security reasons, we recommend that the Admin API (`/_synapse/admin/...`) should be hidden from public view using a reverse proxy. This means you should typically query the Admin API from a terminal on the machine which runs Synapse.

Once you have your `access_token`, you will need to authenticate each request to an Admin API endpoint by providing the token as either a query parameter or a request header. 

**To add it as a request header in cURL:**
```bash
curl --header "Authorization: Bearer <access_token>" <the_rest_of_your_API_request>
```

**Example:**
Suppose we want to query the account of the user `@foo:bar.com`. We need an admin access token (e.g. `syt_AjfVef2_L33JNpafeif_0feKJfeaf0CQpoZk`), and we need to know which port Synapse’s client listener is listening on (e.g. `8008`). Then we can use the following command to request the account information from the Admin API.

```bash
curl --header "Authorization: Bearer syt_AjfVef2_L33JNpafeif_0feKJfeaf0CQpoZk" \
     -X GET http://127.0.0.1:8008/_synapse/admin/v2/users/@foo:bar.com
```

For more details on access tokens in Matrix, please refer to the complete matrix spec documentation.

---

## ⚖️ Account validity API
*Note: This API is disabled when MSC3861 is enabled. See #15582*

This API allows a server administrator to manage the validity of an account. To use it, you must enable the account validity feature (under `account_validity`) in Synapse’s configuration.

To use it, you will need to authenticate by providing an `access_token` for a server admin: see Admin API.

### Renew account
This API extends the validity of an account by as much time as configured in the `period` parameter from the `account_validity` configuration.

**The API is:** `POST /_synapse/admin/v1/account_validity/validity`

**With the following body:**
```json
{
  "user_id": "<user ID for the account to renew>",
  "expiration_ts": 0,
  "enable_renewal_emails": true
}
```
*   `expiration_ts` is an optional parameter and overrides the expiration date, which otherwise defaults to `now + validity period`.
*   `enable_renewal_emails` is also an optional parameter and enables/disables sending renewal emails to the user. Defaults to `true`.

**The API returns** with the new expiration date for this account, as a timestamp in milliseconds since epoch:
```json
{
  "expiration_ts": 0
}
```

---

## ⚙️ Background Updates API
This API allows a server administrator to manage the background updates being run against the database.

### Status
This API gets the current status of the background updates.

**The API is:** `GET /_synapse/admin/v1/background_updates/status`

**Returning:**
```json
{
  "enabled": true,
  "current_updates": {
    "<db_name>": {
      "name": "<background_update_name>",
      "total_item_count": 50,
      "total_duration_ms": 10000.0,
      "average_items_per_ms": 2.2
    }
  }
}
```
*   `enabled` whether the background updates are enabled or disabled.
*   `db_name` the database name (usually Synapse is configured with a single database named ‘master’).
*   **For each update:**
    *   `name` the name of the update.
    *   `total_item_count` total number of “items” processed (the meaning of ‘items’ depends on the update in question).
    *   `total_duration_ms` how long the background process has been running, not including time spent sleeping.
    *   `average_items_per_ms` how many items are processed per millisecond based on an exponential average.

### Enabled
This API allows pausing background updates. Background updates should not be paused for significant periods of time, as this can affect the performance of Synapse.

*   **Note:** This won’t persist over restarts.
*   **Note:** This won’t cancel any update query that is currently running. This is usually fine since most queries are short lived, except for `CREATE INDEX` background updates which won’t be cancelled once started.

**The API is:** `POST /_synapse/admin/v1/background_updates/enabled`

**With the following body:**
```json
{
  "enabled": false
}
```
*   `enabled` sets whether the background updates are enabled or disabled.

**The API returns the enabled param:**
```json
{
  "enabled": false
}
```
There is also a `GET` version which returns the enabled state.

### Run
This API schedules a specific background update to run. The job starts immediately after calling the API.

**The API is:** `POST /_synapse/admin/v1/background_updates/start_job`

**With the following body:**
```json
{
  "job_name": "populate_stats_process_rooms"
}
```
The following JSON body parameters are available:
*   `job_name` - A string which job to run. Valid values are:
    *   `populate_stats_process_rooms` - Recalculate the stats for all rooms.
    *   `regenerate_directory` - Recalculate the user directory if it is stale or out of sync.

---

## 🔍 Fetch Event API
The fetch event API allows admins to fetch an event regardless of their membership in the room it originated in. To use it, you will need to authenticate by providing an `access_token` for a server admin: see Admin API.

**Request:** `GET /_synapse/admin/v1/fetch_event/<event_id>`

**The API returns a JSON body like the following:**

**Response:**
```json
{
  "event": {
    "auth_events": [
      "$WhLChbYg6atHuFRP7cUd95naUtc8L0f7fqeizlsUVvc",
      "$9Wj8dt02lrNEWweeq-KjRABUYKba0K9DL2liRvsAdtQ",
      "$qJxBFxBt8_ODd9b3pgOL_jXP98S_igc1_kizuPSZFi4"
    ],
    "content": {
      "body": "Hey now",
      "msgtype": "m.text"
    },
    "depth": 6,
    "event_id": "$hJ_kcXbVMcI82JDrbqfUJIHu61tJD86uIFJ_8hNHi7s",
    "hashes": {
      "sha256": "LiNw8DtrRVf55EgAH8R42Wz7WCJUqGsPt2We6qZO5Rg"
    },
    "origin_server_ts": 799,
    "prev_events": [
      "$cnSUrNMnC3Ywh9_W7EquFxYQjC_sT3BAAVzcUVxZq1g"
    ],
    "room_id": "!aIhKToCqgPTBloWMpf:test",
    "sender": "@user:test",
    "signatures": {
      "test": {
        "ed25519:a_lPym": "7mqSDwK1k7rnw34Dd8Fahu0rhPW7jPmcWPRtRDoEN9Yuv+BCM2+Rfdpv2MjxNKy3AYDEBwUwYEuaKMBaEMiKAQ"
      }
    },
    "type": "m.room.message",
    "unsigned": {
      "age_ts": 799
    }
  }
}
```

---

## 🚩 Event Reports API

### Show reported events
This API returns information about reported events. To use it, you will need to authenticate by providing an `access_token` for a server admin: see Admin API.

**The api is:** `GET /_synapse/admin/v1/event_reports?from=0&limit=10`

**It returns a JSON body like the following:**
```json
{
  "event_reports": [
    {
      "event_id": "$bNUFCwGzWca1meCGkjp-zwslF-GfVcXukvRLI1_FaVY",
      "id": 2,
      "reason": "foo",
      "score": -100,
      "received_ts": 1570897107409,
      "canonical_alias": "#alias1:matrix.org",
      "room_id": "!ERAgBpSOcCCuTJqQPk:matrix.org",
      "name": "Matrix HQ",
      "sender": "@foobar:matrix.org",
      "user_id": "@foo:matrix.org"
    },
    {
      "event_id": "$3IcdZsDaN_En-S1DF4EMCy3v4gNRKeOJs8W5qTOKj4I",
      "id": 3,
      "reason": "bar",
      "score": -100,
      "received_ts": 1598889612059,
      "canonical_alias": "#alias2:matrix.org",
      "room_id": "!eGvUQuTCkHGVwNMOjv:matrix.org",
      "name": "Your room name here",
      "sender": "@foobar:matrix.org",
      "user_id": "@bar:matrix.org"
    }
  ],
  "next_token": 2,
  "total": 4
}
```
To paginate, check for `next_token` and if present, call the endpoint again with `from` set to the value of `next_token`. This will return a new page. If the endpoint does not return a `next_token` then there are no more reports to paginate through.

**URL parameters:**
*   `limit` : integer - Is optional but is used for pagination, denoting the maximum number of items to return in this call. Defaults to `100`.
*   `from` : integer - Is optional but used for pagination, denoting the offset in the returned results. This should be treated as an opaque value and not explicitly set to anything other than the return value of `next_token` from a previous call. Defaults to `0`.
*   `dir` : string - Direction of event report order. Whether to fetch the most recent first (`b`) or the oldest first (`f`). Defaults to `b`.
*   `user_id` : optional string - Filter by the user ID of the reporter. This is the user who reported the event and wrote the reason.
*   `room_id` : optional string - Filter by room id.
*   `event_sender_user_id` : optional string - Filter by the sender of the reported event. This is the user who the report was made against.

**Response Fields:**
The following fields are returned in the JSON response body:
*   `id` : integer - ID of event report.
*   `received_ts` : integer - The timestamp (in milliseconds since the unix epoch) when this report was sent.
*   `room_id` : string - The ID of the room in which the event being reported is located.
*   `name` : string - The name of the room.
*   `event_id` : string - The ID of the reported event.
*   `user_id` : string - This is the user who reported the event and wrote the reason.
*   `reason` : string - Comment made by the `user_id` in this report. May be blank or `null`.
*   `score` : integer - Content is reported based upon a negative score, where -100 is “most offensive” and 0 is “inoffensive”. May be `null`.
*   `sender` : string - This is the ID of the user who sent the original message/event that was reported.
*   `canonical_alias` : string - The canonical alias of the room. `null` if the room does not have a canonical alias set.
*   `next_token` : integer - Indication for pagination. See above.
*   `total` : integer - Total number of event reports related to the query (`user_id` and `room_id`).

### Show details of a specific event report
This API returns information about a specific event report.

**The api is:** `GET /_synapse/admin/v1/event_reports/<report_id>`

**It returns a JSON body like the following:**
```json
{
  "event_id": "$bNUFCwGzWca1meCGkjp-zwslF-GfVcXukvRLI1_FaVY",
  "event_json": {
    "auth_events": [
      "$YK4arsKKcc0LRoe700pS8DSjOvUT4NDv0HfInlMFw2M",
      "$oggsNXxzPFRE3y53SUNd7nsj69-QzKv03a1RucHu-ws"
    ],
    "content": {
      "body": "matrix.org: This Week in Matrix",
      "format": "org.matrix.custom.html",
      "formatted_body": "<strong>matrix.org</strong>:<br><a href=\"https://matrix.org/blog/\"><strong>This Week in Matrix</strong></a>",
      "msgtype": "m.notice"
    },
    "depth": 546,
    "hashes": {
      "sha256": "xK1//xnmvHJIOvbgXlkI8eEqdvoMmihVDJ9J4SNlsAw"
    },
    "origin_server_ts": 1592291711430,
    "prev_events": [
      "$YK4arsKKcc0LRoe700pS8DSjOvUT4NDv0HfInlMFw2M"
    ],
    "prev_state": [],
    "room_id": "!ERAgBpSOcCCuTJqQPk:matrix.org",
    "sender": "@foobar:matrix.org",
    "signatures": {
      "matrix.org": {
        "ed25519:a_JaEG": "cs+OUKW/iHx5pEidbWxh0UiNNHwe46Ai9LwNz+Ah16aWDNszVIe2gaAcVZfvNsBhakQTew51tlKmL2kspXk/Dg"
      }
    },
    "type": "m.room.message",
    "unsigned": {
      "age_ts": 1592291711430
    }
  },
  "id": <report_id>,
  "reason": "foo",
  "score": -100,
  "received_ts": 1570897107409,
  "canonical_alias": "#alias1:matrix.org",
  "room_id": "!ERAgBpSOcCCuTJqQPk:matrix.org",
  "name": "Matrix HQ",
  "sender": "@foobar:matrix.org",
  "user_id": "@foo:matrix.org"
}
```

**URL parameters:**
*   `report_id` : string - The ID of the event report.

**Response Fields:**
The following fields are returned in the JSON response body:
*   `id` : integer - ID of event report.
*   `received_ts` : integer - The timestamp (in milliseconds since the unix epoch) when this report was sent.
*   `room_id` : string - The ID of the room in which the event being reported is located.
*   `name` : string - The name of the room.
*   `event_id` : string - The ID of the reported event.
*   `user_id` : string - This is the user who reported the event and wrote the reason.
*   `reason` : string - Comment made by the `user_id` in this report. May be blank.
*   `score` : integer - Content is reported based upon a negative score, where -100 is “most offensive” and 0 is “inoffensive”.
*   `sender` : string - This is the ID of the user who sent the original message/event that was reported.
*   `canonical_alias` : string - The canonical alias of the room. `null` if the room does not have a canonical alias set.
*   `event_json` : object - Details of the original event that was reported.

### Delete a specific event report
This API deletes a specific event report. If the request is successful, the response body will be an empty JSON object.

**The api is:** `DELETE /_synapse/admin/v1/event_reports/<report_id>`

**URL parameters:**
*   `report_id` : string - The ID of the event report.

---

## 🧪 Experimental Features API
This API allows a server administrator to enable or disable some experimental features on a per-user basis. The currently supported features are:
*   `MSC3881`: enable remotely toggling push notifications for another client
*   `MSC3575`: enable experimental sliding sync support
*   `MSC4222`: adding `state_after` to sync v2

To use it, you will need to authenticate by providing an `access_token` for a server admin: see Admin API.

### Enabling/Disabling Features
This API allows a server administrator to enable experimental features for a given user. The request must provide a body containing the user id and listing the features to enable/disable in the following format:
```json
{
  "features": {
    "msc3026": true,
    "msc3881": true
  }
}
```
where `true` is used to enable the feature, and `false` is used to disable the feature.

**The API is:** `PUT /_synapse/admin/v1/experimental_features/<user_id>`

### Listing Enabled Features
To list which features are enabled/disabled for a given user send a request to the following API:

**The API is:** `GET /_synapse/admin/v1/experimental_features/<user_id>`

It will return a list of possible features and indicate whether they are enabled or disabled for the user like so:
```json
{
  "features": {
    "msc3026": true,
    "msc3881": false,
    "msc3967": false
  }
}
```

---

## 🖼️ Media Management API
These APIs allow extracting media information from the homeserver. Details about the format of the `media_id` and storage of the media in the file system are documented under "media repository".

To use it, you will need to authenticate by providing an `access_token` for a server admin: see Admin API.

### List all media in a room
This API gets a list of known media in a room. However, it only shows media from unencrypted events or rooms.

**The API is:** `GET /_synapse/admin/v1/room/<room_id>/media`

**The API returns a JSON body like the following:**
```json
{
  "local": [
    "mxc://localhost/xwvutsrqponmlkjihgfedcba",
    "mxc://localhost/abcdefghijklmnopqrstuvwx"
  ],
  "remote": [
    "mxc://matrix.org/xwvutsrqponmlkjihgfedcba",
    "mxc://matrix.org/abcdefghijklmnopqrstuvwx"
  ]
}
```

### List all media uploaded by a user
Listing all media that has been uploaded by a local user can be achieved through the use of the List media uploaded by a user Admin API.

### Query a piece of media by ID
This API returns information about a piece of local or cached remote media given the origin server name and media id. If information is requested for remote media which is not cached the endpoint will return 404.

**Request:** `GET /_synapse/admin/v1/media/<origin>/<media_id>`

**The API returns a JSON body with media info like the following:**

**Response:**
```json
{
  "media_info": {
    "media_origin": "remote.com",
    "user_id": null,
    "media_id": "sdginwegWEG",
    "media_type": "img/png",
    "media_length": 67,
    "upload_name": "test.png",
    "created_ts": 300,
    "filesystem_id": "wgeweg",
    "url_cache": null,
    "last_access_ts": 400,
    "quarantined_by": null,
    "authenticated": false,
    "safe_from_quarantine": null,
    "sha256": "ebf4f635a17d10d6eb46ba680b70142419aa3220f228001a036d311a22ee9d2a"
  }
}
```

### Quarantine media
Quarantining media means that it is marked as inaccessible by users. It applies to any local media, and any locally-cached copies of remote media. The media file itself (and any thumbnails) is not deleted from the server.

Since Synapse 1.128.0, hashes of uploaded media are tracked. If this media is quarantined, Synapse will:
*   Quarantine any media with a matching hash that has already been uploaded.
*   Quarantine any future media.
*   Quarantine any existing cached remote media.
*   Quarantine any future remote media.

#### Downloading quarantined media
Normally, when media is quarantined, it will return a 404 error when downloaded. Admins can bypass by adding `?admin_unsafely_bypass_quarantine=true` to the normal download URL. Bypassing the quarantine check is not recommended. Media is typically quarantined to prevent harmful content from being served to users, which includes admins. Only set the bypass parameter if you intentionally want to access potentially harmful content.

Non-admin users cannot bypass quarantine checks, even when specifying the above query parameter.

#### Quarantining media by ID
This API quarantines a single piece of local or remote media.

**Request:** `POST /_synapse/admin/v1/media/quarantine/<server_name>/<media_id>`
`{}`
Where `server_name` is in the form of `example.org`, and `media_id` is in the form of `abcdefg12345...`.

**Response:** `{}`

#### Remove media from quarantine by ID
This API removes a single piece of local or remote media from quarantine.

**Request:** `POST /_synapse/admin/v1/media/unquarantine/<server_name>/<media_id>`
`{}`
Where `server_name` is in the form of `example.org`, and `media_id` is in the form of `abcdefg12345...`.

**Response:** `{}`

#### Quarantining media in a room
This API quarantines all local and remote media in a room.

**Request:** `POST /_synapse/admin/v1/room/<room_id>/media/quarantine`
`{}`
Where `room_id` is in the form of `!roomid12345:example.org`.

**Response:**
```json
{
  "num_quarantined": 10
}
```
The following fields are returned in the JSON response body:
*   `num_quarantined` : integer - The number of media items successfully quarantined

Note that there is a legacy endpoint, `POST /_synapse/admin/v1/quarantine_media/<room_id>`, that operates the same. However, it is deprecated and may be removed in a future release.

#### Quarantining all media of a user
This API quarantines all local media that a local user has uploaded. That is to say, if you would like to quarantine media uploaded by a user on a remote homeserver, you should instead use one of the other APIs.

**Request:** `POST /_synapse/admin/v1/user/<user_id>/media/quarantine`
`{}`

**URL Parameters:**
*   `user_id` : string - User ID in the form of `@bob:example.org`

**Response:**
```json
{
  "num_quarantined": 10
}
```
The following fields are returned in the JSON response body:
*   `num_quarantined` : integer - The number of media items successfully quarantined

### Protecting media from being quarantined
This API protects a single piece of local media from being quarantined using the above APIs. This is useful for sticker packs and other shared media which you do not want to get quarantined, especially when quarantining media in a room.

**Request:** `POST /_synapse/admin/v1/media/protect/<media_id>`
`{}`
Where `media_id` is in the form of `abcdefg12345...`.

**Response:** `{}`

### Unprotecting media from being quarantined
This API reverts the protection of a media.

**Request:** `POST /_synapse/admin/v1/media/unprotect/<media_id>`
`{}`
Where `media_id` is in the form of `abcdefg12345...`.

**Response:** `{}`

### Delete local media
This API deletes the local media from the disk of your own server. This includes any local thumbnails and copies of media downloaded from remote homeservers. This API will not affect media that has been uploaded to external media repositories (e.g `https://github.com/turt2live/matrix-media-repo/`). See also Purge Remote Media API.

#### Delete a specific local media
Delete a specific `media_id`.

**Request:** `DELETE /_synapse/admin/v1/media/<server_name>/<media_id>`
`{}`

**URL Parameters:**
*   `server_name` : string - The name of your local server (e.g `matrix.org`)
*   `media_id` : string - The ID of the media (e.g `abcdefghijklmnopqrstuvwx`)

**Response:**
```json
{
  "deleted_media": [
    "abcdefghijklmnopqrstuvwx"
  ],
  "total": 1
}
```
The following fields are returned in the JSON response body:
*   `deleted_media` : an array of strings - List of deleted media_id
*   `total` : integer - Total number of deleted media_id

#### Delete local media by date or size
**Request:** `POST /_synapse/admin/v1/media/delete?before_ts=<before_ts>`
`{}`
Deprecated in Synapse v1.78.0: This API is available at the deprecated endpoint: `POST /_synapse/admin/v1/media/<server_name>/delete?before_ts=<before_ts>` `{}`

**URL Parameters:**
*   `server_name` : string - The name of your local server (e.g `matrix.org`). Deprecated in Synapse v1.78.0.
*   `before_ts` : string representing a positive integer - Unix timestamp in milliseconds. Files that were last used before this timestamp will be deleted. It is the timestamp of last access, not the timestamp when the file was created.
*   `size_gt` : Optional - string representing a positive integer - Size of the media in bytes. Files that are larger will be deleted. Defaults to `0`.
*   `keep_profiles` : Optional - string representing a boolean - Switch to also delete files that are still used in image data (e.g user profile, room avatar). If `false` these files will be deleted. Defaults to `true`.

**Response:**
```json
{
  "deleted_media": [
    "abcdefghijklmnopqrstuvwx",
    "abcdefghijklmnopqrstuvwz"
  ],
  "total": 2
}
```
The following fields are returned in the JSON response body:
*   `deleted_media` : an array of strings - List of deleted media_id
*   `total` : integer - Total number of deleted media_id

### Purge Remote Media API
The purge remote media API allows server admins to purge old cached remote media.

**The API is:** `POST /_synapse/admin/v1/purge_media_cache?before_ts=<unix_timestamp_in_ms>`
`{}`

**URL Parameters:**
*   `before_ts` : string representing a positive integer - Unix timestamp in milliseconds. All cached media that was last accessed before this timestamp will be removed.

**Response:**
```json
{
  "deleted": 10
}
```
The following fields are returned in the JSON response body:
*   `deleted` : integer - The number of media items successfully deleted

If the user re-requests purged remote media, synapse will re-request the media from the originating server.

---

## 🧹 Purge History API
The purge history API allows server admins to purge historic events from their database, reclaiming disk space. Depending on the amount of history being purged a call to the API may take several minutes or longer. During this period users will not be able to paginate further back in the room from the point being purged from. Note that Synapse requires at least one message in each room, so it will never delete the last message in a room. To use it, you will need to authenticate by providing an `access_token` for a server admin: see Admin API.

**The API is:** `POST /_synapse/admin/v1/purge_history/<room_id>[/<event_id>]`

By default, events sent by local users are not deleted, as they may represent the only copies of this content in existence. (Events sent by remote users are deleted.) Room state data (such as joins, leaves, topic) is always preserved. To delete local message events as well, set `delete_local_events` in the body:
```json
{
  "delete_local_events": true
}
```
The caller must specify the point in the room to purge up to. This can be specified by including an `event_id` in the URI, or by setting a `purge_up_to_event_id` or `purge_up_to_ts` in the request body. If an event id is given, that event (and others at the same graph depth) will be retained. If `purge_up_to_ts` is given, it should be a timestamp since the unix epoch, in milliseconds.

The API starts the purge running, and returns immediately with a JSON body with a purge id:
```json
{
  "purge_id": "<opaque id>"
}
```

### Purge status query
It is possible to poll for updates on recent purges with a second API;

**The API is:** `GET /_synapse/admin/v1/purge_history_status/<purge_id>`

**This API returns a JSON body like the following:**
```json
{
  "status": "active"
}
```
The status will be one of `active`, `complete`, or `failed`. If status is `failed` there will be a string error with the error message.
