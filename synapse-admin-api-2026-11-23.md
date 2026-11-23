# Synapse Admin API reference (v1.165.0)

The Admin API pages of the official Synapse documentation, release [v1.165.0](https://github.com/element-hq/synapse/releases/tag/v1.165.0) (commit [`0adca7d`](https://github.com/element-hq/synapse/tree/0adca7d1f768f45edf9452e423d32e46d4688767/docs)), collected on 2026-11-23 into one file for this repository.

- **Rendered original:** <https://element-hq.github.io/synapse/v1.165/usage/administration/admin_api/index.html>
- **Pages and order:** the "Admin API" section of the docs' `SUMMARY.md`.
- **What changed:** nothing in the text. Each page sits under a `##` section named as in `SUMMARY.md`, so its headings are two levels lower. Links to another page in this file, or to an anchor, point into this file; other relative links point to the v1.165.0 docs site.
- **License:** Synapse, including its documentation, is licensed under the [AGPL-3.0](https://github.com/element-hq/synapse/blob/v1.165.0/LICENSE-AGPL-3.0), like this repository.
- **Updating:** replace the file with a newer release's pages; don't edit it by hand.

## Contents

- [Admin API: introduction](#admin-api-introduction)
- [Account Validity](#account-validity)
- [Background Updates](#background-updates)
- [Fetch Event](#fetch-event)
- [Event Reports](#event-reports)
- [Experimental Features](#experimental-features)
- [Media](#media)
- [Purge History](#purge-history)
- [Register Users](#register-users)
- [Registration Tokens](#registration-tokens)
- [Manipulate Room Membership](#manipulate-room-membership)
- [Rooms](#rooms)
- [Scheduled tasks](#scheduled-tasks)
- [Server Notices](#server-notices)
- [Server Announcements](#server-announcements)
- [Statistics](#statistics)
- [Users](#users)
- [Server Version](#server-version)
- [Federation](#federation)
- [Client-Server API Extensions](#client-server-api-extensions)

---

## Admin API: introduction

*Source: [`docs/usage/administration/admin_api/README.md`](https://github.com/element-hq/synapse/blob/v1.165.0/docs/usage/administration/admin_api/README.md) · [rendered](https://element-hq.github.io/synapse/v1.165/usage/administration/admin_api/index.html)*

### The Admin API

#### Authenticate as a server admin

Many of the API calls in the admin api will require an `access_token` for a
server admin. (Note that a server admin is distinct from a room admin.)

An existing user can be marked as a server admin by updating the database directly.

Check your [database settings](https://element-hq.github.io/synapse/v1.165/usage/configuration/config_documentation.html#database) in the configuration file, connect to the correct database using either `psql [database name]` (if using PostgreSQL) or `sqlite3 path/to/your/database.db` (if using SQLite) and elevate the user `@foo:bar.com` to administrator.
```sql
UPDATE users SET admin = 1 WHERE name = '@foo:bar.com';
```

A new server admin user can also be created using the `register_new_matrix_user`
command. This is a script that is distributed as part of synapse. It is possibly
already on your `$PATH` depending on how Synapse was installed.

Finding your user's `access_token` is client-dependent, but will usually be shown in the client's settings.

#### Making an Admin API request
For security reasons, we [recommend](https://element-hq.github.io/synapse/v1.165/reverse_proxy.html#synapse-administration-endpoints)
that the Admin API (`/_synapse/admin/...`) should be hidden from public view using a
reverse proxy. This means you should typically query the Admin API from a terminal on
the machine which runs Synapse.

Once you have your `access_token`, you will need to authenticate each request to an Admin API endpoint by
providing the token as either a query parameter or a request header. To add it as a request header in cURL:

```sh
curl --header "Authorization: Bearer <access_token>" <the_rest_of_your_API_request>
```

For example, suppose we want to
[query the account](#query-user-account) of the user
`@foo:bar.com`. We need an admin access token (e.g.
`syt_AjfVef2_L33JNpafeif_0feKJfeaf0CQpoZk`), and we need to know which port
Synapse's [`client` listener](https://element-hq.github.io/synapse/v1.165/usage/configuration/config_documentation.html#listeners) is listening
on (e.g. `8008`). Then we can use the following command to request the account
information from the Admin API.

```sh
curl --header "Authorization: Bearer syt_AjfVef2_L33JNpafeif_0feKJfeaf0CQpoZk" -X GET http://127.0.0.1:8008/_synapse/admin/v2/users/@foo:bar.com
```

For more details on access tokens in Matrix, please refer to the complete
[matrix spec documentation](https://matrix.org/docs/spec/client_server/r0.6.1#using-access-tokens).

---

## Account Validity

*Source: [`docs/admin_api/account_validity.md`](https://github.com/element-hq/synapse/blob/v1.165.0/docs/admin_api/account_validity.md) · [rendered](https://element-hq.github.io/synapse/v1.165/admin_api/account_validity.html)*

### Account validity API

**Note:** This API is disabled when Matrix Authentication Service integration is enabled. [See #15582](https://github.com/matrix-org/synapse/pull/15582)

This API allows a server administrator to manage the validity of an account. To
use it, you must enable the account validity feature (under
`account_validity`) in Synapse's configuration.

To use it, you will need to authenticate by providing an `access_token`
for a server admin: see [Admin API](#admin-api-introduction).

#### Renew account

This API extends the validity of an account by as much time as configured in the
`period` parameter from the `account_validity` configuration.

The API is:

```
POST /_synapse/admin/v1/account_validity/validity
```

with the following body:

```json
{
    "user_id": "<user ID for the account to renew>",
    "expiration_ts": 0,
    "enable_renewal_emails": true
}
```


`expiration_ts` is an optional parameter and overrides the expiration date,
which otherwise defaults to now + validity period.

`enable_renewal_emails` is also an optional parameter and enables/disables
sending renewal emails to the user. Defaults to true.

The API returns with the new expiration date for this account, as a timestamp in
milliseconds since epoch:

```json
{
    "expiration_ts": 0
}
```

---

## Background Updates

*Source: [`docs/usage/administration/admin_api/background_updates.md`](https://github.com/element-hq/synapse/blob/v1.165.0/docs/usage/administration/admin_api/background_updates.md) · [rendered](https://element-hq.github.io/synapse/v1.165/usage/administration/admin_api/background_updates.html)*

### Background Updates API

This API allows a server administrator to manage the background updates being
run against the database.

#### Status

This API gets the current status of the background updates.


The API is:

```
GET /_synapse/admin/v1/background_updates/status
```

Returning:

```json
{
    "enabled": true,
    "current_updates": {
        "<db_name>": {
            "name": "<background_update_name>",
            "total_item_count": 50,
            "total_duration_ms": 10000.0,
            "average_items_per_ms": 2.2,
        },
    }
}
```

`enabled` whether the background updates are enabled or disabled.

`db_name` the database name (usually Synapse is configured with a single database named 'master').

For each update:

`name` the name of the update.
`total_item_count` total number of "items" processed (the meaning of 'items' depends on the update in question).
`total_duration_ms` how long the background process has been running, not including time spent sleeping.
`average_items_per_ms` how many items are processed per millisecond based on an exponential average.


#### Enabled

This API allows pausing background updates.

Background updates should *not* be paused for significant periods of time, as
this can affect the performance of Synapse.

*Note*: This won't persist over restarts.

*Note*: This won't cancel any update query that is currently running. This is
usually fine since most queries are short lived, except for `CREATE INDEX`
background updates which won't be cancelled once started.


The API is:

```
POST /_synapse/admin/v1/background_updates/enabled
```

with the following body:

```json
{
    "enabled": false
}
```

`enabled` sets whether the background updates are enabled or disabled.

The API returns the `enabled` param.

```json
{
    "enabled": false
}
```

There is also a `GET` version which returns the `enabled` state.


#### Run

This API schedules a specific background update to run. The job starts immediately after calling the API.


The API is:

```
POST /_synapse/admin/v1/background_updates/start_job
```

with the following body:

```json
{
    "job_name": "populate_stats_process_rooms"
}
```

The following JSON body parameters are available:

- `job_name` - A string which job to run. Valid values are:
  - `populate_stats_process_rooms` - Recalculate the stats for all rooms.
  - `regenerate_directory` - Recalculate the [user directory](https://element-hq.github.io/synapse/v1.165/user_directory.html) if it is stale or out of sync.
  - `event_resign` - Re-sign all locally-sent events with the current signing key. This is useful after rotating the server's signing key to ensure all historical events are signed with the new key. Optional additional parameters:
    - `old_key` - Only re-sign events whose signature verifies against this key. Format: `"ed25519:key_id base64_public_key"` (e.g. `"ed25519:my_old_key XGX0JRS2Af3be3knz2fBiRbApjm2Dh61gXDJA8kcJNI"`).
    - `before_ts` - Only re-sign events with a `received_ts` less than this value (milliseconds since the epoch).

---

## Fetch Event

*Source: [`docs/admin_api/fetch_event.md`](https://github.com/element-hq/synapse/blob/v1.165.0/docs/admin_api/fetch_event.md) · [rendered](https://element-hq.github.io/synapse/v1.165/admin_api/fetch_event.html)*

### Fetch Event API

The fetch event API allows admins to fetch an event regardless of their membership in the room it
originated in.

To use it, you will need to authenticate by providing an `access_token`
for a server admin: see [Admin API](#admin-api-introduction).

Request:
```http
GET /_synapse/admin/v1/fetch_event/<event_id>
```

The API returns a JSON body like the following:

Response:
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

## Event Reports

*Source: [`docs/admin_api/event_reports.md`](https://github.com/element-hq/synapse/blob/v1.165.0/docs/admin_api/event_reports.md) · [rendered](https://element-hq.github.io/synapse/v1.165/admin_api/event_reports.html)*

### Show reported events

This API returns information about reported events.

To use it, you will need to authenticate by providing an `access_token`
for a server admin: see [Admin API](#admin-api-introduction).

The api is:
```
GET /_synapse/admin/v1/event_reports?from=0&limit=10
```

It returns a JSON body like the following:

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

To paginate, check for `next_token` and if present, call the endpoint again with `from`
set to the value of `next_token`. This will return a new page.

If the endpoint does not return a `next_token` then there are no more reports to
paginate through.

**URL parameters:**

* `limit`: integer - Is optional but is used for pagination, denoting the maximum number
  of items to return in this call. Defaults to `100`.
* `from`: integer - Is optional but used for pagination, denoting the offset in the
  returned results. This should be treated as an opaque value and not explicitly set to
  anything other than the return value of `next_token` from a previous call. Defaults to `0`.
* `dir`: string - Direction of event report order. Whether to fetch the most recent
  first (`b`) or the oldest first (`f`). Defaults to `b`.
* `user_id`: optional string - Filter by the user ID of the reporter. This is the user who reported the event
   and wrote the reason.
* `room_id`: optional string - Filter by room id.
* `event_sender_user_id`: optional string - Filter by the sender of the reported event. This is the user who 
   the report was made against.

**Response**

The following fields are returned in the JSON response body:

* `id`: integer - ID of event report.
* `received_ts`: integer - The timestamp (in milliseconds since the unix epoch) when this
  report was sent.
* `room_id`: string - The ID of the room in which the event being reported is located.
* `name`: string - The name of the room.
* `event_id`: string - The ID of the reported event.
* `user_id`: string - This is the user who reported the event and wrote the reason.
* `reason`: string - Comment made by the `user_id` in this report. May be blank or `null`.
* `score`: integer - Content is reported based upon a negative score, where -100 is
  "most offensive" and 0 is "inoffensive". May be `null`.
* `sender`: string - This is the ID of the user who sent the original message/event that
  was reported.
* `canonical_alias`: string - The canonical alias of the room. `null` if the room does not
  have a canonical alias set.
* `next_token`: integer - Indication for pagination. See above.
* `total`: integer - Total number of event reports related to the query
  (`user_id` and `room_id`).

### Show details of a specific event report

This API returns information about a specific event report.

The api is:
```
GET /_synapse/admin/v1/event_reports/<report_id>
```

It returns a JSON body like the following:

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

* `report_id`: string - The ID of the event report.

**Response**

The following fields are returned in the JSON response body:

* `id`: integer - ID of event report.
* `received_ts`: integer - The timestamp (in milliseconds since the unix epoch) when this
  report was sent.
* `room_id`: string - The ID of the room in which the event being reported is located.
* `name`: string - The name of the room.
* `event_id`: string - The ID of the reported event.
* `user_id`: string - This is the user who reported the event and wrote the reason.
* `reason`: string - Comment made by the `user_id` in this report. May be blank.
* `score`: integer - Content is reported based upon a negative score, where -100 is
  "most offensive" and 0 is "inoffensive".
* `sender`: string - This is the ID of the user who sent the original message/event that
  was reported.
* `canonical_alias`: string - The canonical alias of the room. `null` if the room does not
  have a canonical alias set.
* `event_json`: object - Details of the original event that was reported.

### Delete a specific event report

This API deletes a specific event report. If the request is successful, the response body
will be an empty JSON object.

The api is:
```
DELETE /_synapse/admin/v1/event_reports/<report_id>
```

**URL parameters:**

* `report_id`: string - The ID of the event report.

---

## Experimental Features

*Source: [`docs/admin_api/experimental_features.md`](https://github.com/element-hq/synapse/blob/v1.165.0/docs/admin_api/experimental_features.md) · [rendered](https://element-hq.github.io/synapse/v1.165/admin_api/experimental_features.html)*

### Experimental Features API

This API allows a server administrator to enable or disable some experimental features on a per-user
basis. The currently supported features are:
- [MSC3881](https://github.com/matrix-org/matrix-spec-proposals/pull/3881): enable remotely toggling push notifications
for another client
- [MSC3575](https://github.com/matrix-org/matrix-spec-proposals/pull/3575): enable experimental sliding sync support

To use it, you will need to authenticate by providing an `access_token`
for a server admin: see [Admin API](#admin-api-introduction).

#### Enabling/Disabling Features

This API allows a server administrator to enable experimental features for a given user. The request must
provide a body containing the user id and listing the features to enable/disable in the following format:
```json
{
   "features": {
      "msc3026":true,
      "msc3881":true
   }
}
```
where true is  used to enable the feature, and false is used to disable the feature.


The API is:

```
PUT /_synapse/admin/v1/experimental_features/<user_id>
```

#### Listing Enabled Features

To list which features are enabled/disabled for a given user send a request to the following API:

```
GET /_synapse/admin/v1/experimental_features/<user_id>
```

It will return a list of possible features and indicate whether they are enabled or disabled for the
user like so:
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

## Media

*Source: [`docs/admin_api/media_admin_api.md`](https://github.com/element-hq/synapse/blob/v1.165.0/docs/admin_api/media_admin_api.md) · [rendered](https://element-hq.github.io/synapse/v1.165/admin_api/media_admin_api.html)*

### Querying media

These APIs allow extracting media information from the homeserver.

Details about the format of the `media_id` and storage of the media in the file system
are documented under [media repository](https://element-hq.github.io/synapse/v1.165/media_repository.html).

To use it, you will need to authenticate by providing an `access_token`
for a server admin: see [Admin API](#admin-api-introduction).

#### List all media in a room

This API gets a list of known media in a room.
However, it only shows media from unencrypted events or rooms.

The API is:
```
GET /_synapse/admin/v1/room/<room_id>/media
```

The API returns a JSON body like the following:
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

#### List all media uploaded by a user

Listing all media that has been uploaded by a local user can be achieved through
the use of the
[List media uploaded by a user](#list-media-uploaded-by-a-user)
Admin API.

#### Query a piece of media by ID

This API returns information about a piece of local or cached remote media given the origin server name and media id. If
information is requested for remote media which is not cached the endpoint will return 404. 

Request:
```http
GET /_synapse/admin/v1/media/<origin>/<media_id>
```

The API returns a JSON body with media info like the following:

Response:
```json
{
  "media_info": {
    "media_origin": "remote.com",
    "user_id": null,
    "media_id": "sdginwegWEG",
    "media_type": "img/png",
    "media_length": 67,
    "upload_name":  "test.png",
    "created_ts":  300,
    "filesystem_id":  "wgeweg",
    "url_cache": null,
    "last_access_ts": 400,
    "quarantined_by":  null,
    "authenticated":  false,
    "safe_from_quarantine": null,
    "sha256": "ebf4f635a17d10d6eb46ba680b70142419aa3220f228001a036d311a22ee9d2a"
  }
}
```

### Quarantine media

Quarantining media means that it is marked as inaccessible by users. It applies
to any local media, and any locally-cached copies of remote media.

The media file itself (and any thumbnails) is not deleted from the server.

Since Synapse 1.128.0, hashes of uploaded media are tracked. If this media
is quarantined, Synapse will:

 - Quarantine any media with a matching hash that has already been uploaded.
 - Quarantine any future media.
 - Quarantine any existing cached remote media.
 - Quarantine any future remote media.

#### Downloading quarantined media

Normally, when media is quarantined, it will return a 404 error when downloaded.
Admins can bypass this by adding `?admin_unsafely_bypass_quarantine=true`
to the [normal download URL](https://spec.matrix.org/v1.16/client-server-api/#get_matrixclientv1mediadownloadservernamemediaid).

Bypassing the quarantine check is not recommended. Media is typically quarantined
to prevent harmful content from being served to users, which includes admins. Only
set the bypass parameter if you intentionally want to access potentially harmful
content.

Non-admin users cannot bypass quarantine checks, even when specifying the above
query parameter.

#### Quarantining media by ID

This API quarantines a single piece of local or remote media.

Request:

```
POST /_synapse/admin/v1/media/quarantine/<server_name>/<media_id>

{}
```

Where `server_name` is in the form of `example.org`, and `media_id` is in the
form of `abcdefg12345...`.

Response:

```json
{}
```

#### Remove media from quarantine by ID

This API removes a single piece of local or remote media from quarantine.

Request:

```
POST /_synapse/admin/v1/media/unquarantine/<server_name>/<media_id>

{}
```

Where `server_name` is in the form of `example.org`, and `media_id` is in the
form of `abcdefg12345...`.

Response:

```json
{}
```

#### Quarantining media in a room

This API quarantines all local and remote media in a room.

Request:

```
POST /_synapse/admin/v1/room/<room_id>/media/quarantine

{}
```

Where `room_id` is in the form of `!roomid12345:example.org`.

Response:

```json
{
  "num_quarantined": 10
}
```

The following fields are returned in the JSON response body:

* `num_quarantined`: integer - The number of media items successfully quarantined

Note that there is a legacy endpoint, `POST
/_synapse/admin/v1/quarantine_media/<room_id>`, that operates the same.
However, it is deprecated and may be removed in a future release.

#### Quarantining all media of a user

This API quarantines all *local* media that a *local* user has uploaded. That is to say, if
you would like to quarantine media uploaded by a user on a remote homeserver, you should
instead use one of the other APIs.

Request:

```
POST /_synapse/admin/v1/user/<user_id>/media/quarantine

{}
```

URL Parameters

* `user_id`: string - User ID in the form of `@bob:example.org`

Response:

```json
{
  "num_quarantined": 10
}
```

The following fields are returned in the JSON response body:

* `num_quarantined`: integer - The number of media items successfully quarantined

#### Protecting media from being quarantined

This API protects a single piece of local media from being quarantined using the
above APIs. This is useful for sticker packs and other shared media which you do
not want to get quarantined, especially when
[quarantining media in a room](#quarantining-media-in-a-room).

Request:

```
POST /_synapse/admin/v1/media/protect/<media_id>

{}
```

Where `media_id` is in the  form of `abcdefg12345...`.

Response:

```json
{}
```

#### Unprotecting media from being quarantined

This API reverts the protection of a media.

Request:

```
POST /_synapse/admin/v1/media/unprotect/<media_id>

{}
```

Where `media_id` is in the  form of `abcdefg12345...`.

Response:

```json
{}
```

#### Listing quarantined media changes

When media is quarantined or unquarantined, a change record is created in the 
database. This API returns those change records in the order they were created.

**Note**: This API should be considered *best-effort* and expected to have missing or
duplicate records. Currently, this only captures any media explicitly (un)quarantined by
the media quarantine admin API, and the other cases are tracked by
https://github.com/element-hq/synapse/issues/19672. Historical media uploaded before
Synapse 1.152.0 is backfilled in a background update on a best-effort basis.

Each page has a maximum of 100 records. The first page has the oldest records, 
paginating forwards with each `next_batch` value.

Request:

```
GET /_synapse/admin/v1/media/quarantine_changes?from=2
```

Where `from` is the `next_batch` value from a previous request. It is optional
and defaults to the first page (the value `0`).

Response:

```json
{
  "next_batch": 4,
  "changes": [
    { "origin": "example.org", "media_id": "abcdefg12345...", "quarantined": true },
    { "origin": "example.org", "media_id": "abcdefg12345...", "quarantined": false },
    { "origin": "another.example.org", "media_id": "abcdefg12345...", "quarantined": true }
  ]
}
```

### Delete local media
This API deletes the *local* media from the disk of your own server.
This includes any local thumbnails and copies of media downloaded from
remote homeservers.
This API will not affect media that has been uploaded to external
media repositories (e.g https://github.com/turt2live/matrix-media-repo/).
See also [Purge Remote Media API](#purge-remote-media-api).

#### Delete a specific local media
Delete a specific `media_id`.

Request:

```
DELETE /_synapse/admin/v1/media/<server_name>/<media_id>

{}
```

URL Parameters

* `server_name`: string - The name of your local server (e.g `matrix.org`)
* `media_id`: string - The ID of the media (e.g `abcdefghijklmnopqrstuvwx`)

Response:

```json
{
  "deleted_media": [
    "abcdefghijklmnopqrstuvwx"
  ],
  "total": 1
}
```

The following fields are returned in the JSON response body:

* `deleted_media`: an array of strings - List of deleted `media_id`
* `total`: integer - Total number of deleted `media_id`

#### Delete local media by date or size

Request:

```
POST /_synapse/admin/v1/media/delete?before_ts=<before_ts>

{}
```

*Deprecated in Synapse v1.78.0:* This API is available at the deprecated endpoint:

```
POST /_synapse/admin/v1/media/<server_name>/delete?before_ts=<before_ts>

{}
```

URL Parameters

* `server_name`: string - The name of your local server (e.g `matrix.org`). *Deprecated in Synapse v1.78.0.*
* `before_ts`: string representing a positive integer - Unix timestamp in milliseconds.
Files that were last used before this timestamp will be deleted. It is the timestamp of
last access, not the timestamp when the file was created.
* `size_gt`: Optional - string representing a positive integer - Size of the media in bytes.
Files that are larger will be deleted. Defaults to `0`.
* `keep_profiles`: Optional - string representing a boolean - Switch to also delete files
that are still used in image data (e.g user profile, room avatar).
If `false` these files will be deleted. Defaults to `true`.

Response:

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

* `deleted_media`: an array of strings - List of deleted `media_id`
* `total`: integer - Total number of deleted `media_id`

#### Delete media uploaded by a user

You can find details of how to delete multiple media uploaded by a user in
[User Admin API](#delete-media-uploaded-by-a-user-1).

### Purge Remote Media API

The purge remote media API allows server admins to purge old cached remote media.

The API is:

```
POST /_synapse/admin/v1/purge_media_cache?before_ts=<unix_timestamp_in_ms>

{}
```

URL Parameters

* `before_ts`: string representing a positive integer - Unix timestamp in milliseconds.
All cached media that was last accessed before this timestamp will be removed.

Response:

```json
{
  "deleted": 10
}
```

The following fields are returned in the JSON response body:

* `deleted`: integer - The number of media items successfully deleted

If the user re-requests purged remote media, synapse will re-request the media
from the originating server.

---

## Purge History

*Source: [`docs/admin_api/purge_history_api.md`](https://github.com/element-hq/synapse/blob/v1.165.0/docs/admin_api/purge_history_api.md) · [rendered](https://element-hq.github.io/synapse/v1.165/admin_api/purge_history_api.html)*

### Purge History API

The purge history API allows server admins to purge historic events from their
database, reclaiming disk space.

Depending on the amount of history being purged a call to the API may take
several minutes or longer. During this period users will not be able to
paginate further back in the room from the point being purged from.

Note that Synapse requires at least one message in each room, so it will never
delete the last message in a room.

To use it, you will need to authenticate by providing an `access_token`
for a server admin: see [Admin API](#admin-api-introduction).

The API is:

```
POST /_synapse/admin/v1/purge_history/<room_id>[/<event_id>]
```

By default, events sent by local users are not deleted, as they may represent
the only copies of this content in existence. (Events sent by remote users are
deleted.)

Room state data (such as joins, leaves, topic) is always preserved.

To delete local message events as well, set `delete_local_events` in the body:

```json
{
   "delete_local_events": true
}
```

The caller must specify the point in the room to purge up to. This can be
specified by including an event_id in the URI, or by setting a
`purge_up_to_event_id` or `purge_up_to_ts` in the request body. If an event
id is given, that event (and others at the same graph depth) will be retained.
If `purge_up_to_ts` is given, it should be a timestamp since the unix epoch,
in milliseconds.

The API starts the purge running, and returns immediately with a JSON body with
a purge id:

```json
{
    "purge_id": "<opaque id>"
}
```

#### Purge status query

It is possible to poll for updates on recent purges with a second API;

```
GET /_synapse/admin/v1/purge_history_status/<purge_id>
```

This API returns a JSON body like the following:

```json
{
    "status": "active"
}
```

The status will be one of `active`, `complete`, or `failed`.

If `status` is `failed` there will be a string `error` with the error message.

#### Reclaim disk space (Postgres)

To reclaim the disk space and return it to the operating system, you need to run
`VACUUM FULL;` on the database.

<https://www.postgresql.org/docs/current/sql-vacuum.html>

---

## Register Users

*Source: [`docs/admin_api/register_api.md`](https://github.com/element-hq/synapse/blob/v1.165.0/docs/admin_api/register_api.md) · [rendered](https://element-hq.github.io/synapse/v1.165/admin_api/register_api.html)*

### Shared-Secret Registration

**Note:** This API is disabled when Matrix Authentication Service integration is enabled.
Use the [MAS Admin API](https://element-hq.github.io/matrix-authentication-service/topics/admin-api.html) or [the MAS CLI](https://element-hq.github.io/matrix-authentication-service/reference/cli/manage.html#manage-register-user) instead.

This API allows for the creation of users in an administrative and
non-interactive way. This is generally used for bootstrapping a Synapse
instance with administrator accounts.

To authenticate yourself to the server, you will need both the shared secret
([`registration_shared_secret`](https://element-hq.github.io/synapse/v1.165/usage/configuration/config_documentation.html#registration_shared_secret)
in the homeserver configuration), and a one-time nonce. If the registration
shared secret is not configured, this API is not enabled.

To fetch the nonce, you need to request one from the API:

```
> GET /_synapse/admin/v1/register

< {"nonce": "thisisanonce"}
```

Once you have the nonce, you can make a `POST` to the same URL with a JSON
body containing the nonce, username, password, whether they are an admin
(optional, False by default), and a HMAC digest of the content. Also you can
set the displayname (optional, `username` by default).

As an example:

```
> POST /_synapse/admin/v1/register
> {
   "nonce": "thisisanonce",
   "username": "pepper_roni",
   "displayname": "Pepper Roni",
   "password": "pizza",
   "admin": true,
   "mac": "mac_digest_here"
  }

< {
   "access_token": "token_here",
   "user_id": "@pepper_roni:localhost",
   "home_server": "test",
   "device_id": "device_id_here"
  }
```

The MAC is the hex digest output of the HMAC-SHA1 algorithm, with the key being
the shared secret and the content being the nonce, user, password, either the
string "admin" or "notadmin", and optionally the user_type
each separated by NULs.

Here is an easy way to generate the HMAC digest if you have Bash and OpenSSL:

```bash
# Update these values and then paste this code block into a bash terminal
nonce='thisisanonce'
username='pepper_roni'
password='pizza'
admin='admin'
secret='shared_secret'

printf '%s\0%s\0%s\0%s' "$nonce" "$username" "$password" "$admin" |
  openssl sha1 -hmac "$secret" |
  awk '{print $2}'
```

For an example of generation in Python:

```python
import hmac, hashlib

def generate_mac(nonce, user, password, admin=False, user_type=None):

    mac = hmac.new(
      key=shared_secret,
      digestmod=hashlib.sha1,
    )

    mac.update(nonce.encode('utf8'))
    mac.update(b"\x00")
    mac.update(user.encode('utf8'))
    mac.update(b"\x00")
    mac.update(password.encode('utf8'))
    mac.update(b"\x00")
    mac.update(b"admin" if admin else b"notadmin")
    if user_type:
        mac.update(b"\x00")
        mac.update(user_type.encode('utf8'))

    return mac.hexdigest()
```

---

## Registration Tokens

*Source: [`docs/usage/administration/admin_api/registration_tokens.md`](https://github.com/element-hq/synapse/blob/v1.165.0/docs/usage/administration/admin_api/registration_tokens.md) · [rendered](https://element-hq.github.io/synapse/v1.165/usage/administration/admin_api/registration_tokens.html)*

### Registration Tokens

**Note:** This API is disabled when Matrix Authentication Service integration is enabled. Use the [MAS Admin API](https://element-hq.github.io/matrix-authentication-service/topics/admin-api.html) or [the MAS CLI](https://element-hq.github.io/matrix-authentication-service/reference/cli/manage.html#manage-issue-user-registration-token) instead. An easy way to make use of this functionality is provided by [Element Admin](https://element.io/en/server-suite/admin) as part of [ESS](https://element.io/en/server-suite) Community and Pro.

This API allows you to manage tokens which can be used to authenticate
registration requests, as proposed in
[MSC3231](https://github.com/matrix-org/matrix-doc/blob/main/proposals/3231-token-authenticated-registration.md)
and stabilised in version 1.2 of the Matrix specification.
To use it, you will need to enable the `registration_requires_token` config
option, and authenticate by providing an `access_token` for a server admin:
see [Admin API](#admin-api-introduction).


#### Registration token objects

Most endpoints make use of JSON objects that contain details about tokens.
These objects have the following fields:
- `token`: The token which can be used to authenticate registration.
- `uses_allowed`: The number of times the token can be used to complete a
  registration before it becomes invalid.
- `pending`: The number of pending uses the token has. When someone uses
  the token to authenticate themselves, the pending counter is incremented
  so that the token is not used more than the permitted number of times.
  When the person completes registration the pending counter is decremented,
  and the completed counter is incremented.
- `completed`: The number of times the token has been used to successfully
  complete a registration.
- `expiry_time`: The latest time the token is valid. Given as the number of
  milliseconds since 1970-01-01 00:00:00 UTC (the start of the Unix epoch).
  To convert this into a human-readable form you can remove the milliseconds
  and use the `date` command. For example, `date -d '@1625394937'`.


#### List all tokens

Lists all tokens and details about them. If the request is successful, the top
level JSON object will have a `registration_tokens` key which is an array of
registration token objects.

```
GET /_synapse/admin/v1/registration_tokens
```

Optional query parameters:
- `valid`: `true` or `false`. If `true`, only valid tokens are returned.
  If `false`, only tokens that have expired or have had all uses exhausted are
  returned. If omitted, all tokens are returned regardless of validity.

Example:

```
GET /_synapse/admin/v1/registration_tokens
```
```
200 OK

{
    "registration_tokens": [
        {
            "token": "abcd",
            "uses_allowed": 3,
            "pending": 0,
            "completed": 1,
            "expiry_time": null
        },
        {
            "token": "pqrs",
            "uses_allowed": 2,
            "pending": 1,
            "completed": 1,
            "expiry_time": null
        },
        {
            "token": "wxyz",
            "uses_allowed": null,
            "pending": 0,
            "completed": 9,
            "expiry_time": 1625394937000    // 2021-07-04 10:35:37 UTC
        }
    ]
}
```

Example using the `valid` query parameter:

```
GET /_synapse/admin/v1/registration_tokens?valid=false
```
```
200 OK

{
    "registration_tokens": [
        {
            "token": "pqrs",
            "uses_allowed": 2,
            "pending": 1,
            "completed": 1,
            "expiry_time": null
        },
        {
            "token": "wxyz",
            "uses_allowed": null,
            "pending": 0,
            "completed": 9,
            "expiry_time": 1625394937000    // 2021-07-04 10:35:37 UTC
        }
    ]
}
```


#### Get one token

Get details about a single token. If the request is successful, the response
body will be a registration token object.

```
GET /_synapse/admin/v1/registration_tokens/<token>
```

Path parameters:
- `token`: The registration token to return details of.

Example:

```
GET /_synapse/admin/v1/registration_tokens/abcd
```
```
200 OK

{
    "token": "abcd",
    "uses_allowed": 3,
    "pending": 0,
    "completed": 1,
    "expiry_time": null
}
```


#### Create token

Create a new registration token. If the request is successful, the newly created
token will be returned as a registration token object in the response body.

```
POST /_synapse/admin/v1/registration_tokens/new
```

The request body must be a JSON object and can contain the following fields:
- `token`: The registration token. A string of no more than 64 characters that
  consists only of characters matched by the regex `[A-Za-z0-9._~-]`.
  Default: randomly generated.
- `uses_allowed`: The integer number of times the token can be used to complete
  a registration before it becomes invalid.
  Default: `null` (unlimited uses).
- `expiry_time`: The latest time the token is valid. Given as the number of
  milliseconds since 1970-01-01 00:00:00 UTC (the start of the Unix epoch).
  You could use, for example, `date '+%s000' -d 'tomorrow'`.
  Default: `null` (token does not expire).
- `length`: The length of the token randomly generated if `token` is not
  specified. Must be between 1 and 64 inclusive. Default: `16`.

If a field is omitted the default is used.

Example using defaults:

```
POST /_synapse/admin/v1/registration_tokens/new

{}
```
```
200 OK

{
    "token": "0M-9jbkf2t_Tgiw1",
    "uses_allowed": null,
    "pending": 0,
    "completed": 0,
    "expiry_time": null
}
```

Example specifying some fields:

```
POST /_synapse/admin/v1/registration_tokens/new

{
    "token": "defg",
    "uses_allowed": 1
}
```
```
200 OK

{
    "token": "defg",
    "uses_allowed": 1,
    "pending": 0,
    "completed": 0,
    "expiry_time": null
}
```


#### Update token

Update the number of allowed uses or expiry time of a token. If the request is
successful, the updated token will be returned as a registration token object
in the response body.

```
PUT /_synapse/admin/v1/registration_tokens/<token>
```

Path parameters:
- `token`: The registration token to update.

The request body must be a JSON object and can contain the following fields:
- `uses_allowed`: The integer number of times the token can be used to complete
  a registration before it becomes invalid. By setting `uses_allowed` to `0`
  the token can be easily made invalid without deleting it.
  If `null` the token will have an unlimited number of uses.
- `expiry_time`: The latest time the token is valid. Given as the number of
  milliseconds since 1970-01-01 00:00:00 UTC (the start of the Unix epoch).
  If `null` the token will not expire.

If a field is omitted its value is not modified.

Example:

```
PUT /_synapse/admin/v1/registration_tokens/defg

{
    "expiry_time": 4781243146000    // 2121-07-06 11:05:46 UTC
}
```
```
200 OK

{
    "token": "defg",
    "uses_allowed": 1,
    "pending": 0,
    "completed": 0,
    "expiry_time": 4781243146000
}
```


#### Delete token

Delete a registration token. If the request is successful, the response body
will be an empty JSON object.

```
DELETE /_synapse/admin/v1/registration_tokens/<token>
```

Path parameters:
- `token`: The registration token to delete.

Example:

```
DELETE /_synapse/admin/v1/registration_tokens/wxyz
```
```
200 OK

{}
```


#### Errors

If a request fails a "standard error response" will be returned as defined in
the [Matrix Client-Server API specification](https://matrix.org/docs/spec/client_server/r0.6.1#api-standards).

For example, if the token specified in a path parameter does not exist a
`404 Not Found` error will be returned.

```
GET /_synapse/admin/v1/registration_tokens/1234
```
```
404 Not Found

{
    "errcode": "M_NOT_FOUND",
    "error": "No such registration token: 1234"
}
```

---

## Manipulate Room Membership

*Source: [`docs/admin_api/room_membership.md`](https://github.com/element-hq/synapse/blob/v1.165.0/docs/admin_api/room_membership.md) · [rendered](https://element-hq.github.io/synapse/v1.165/admin_api/room_membership.html)*

### Edit Room Membership API

This API allows an administrator to join a user account with a given `user_id`
to a room with a given `room_id_or_alias`. You can only modify the membership of
local users. The server administrator must be in the room and have permission to
invite users.

To use it, you will need to authenticate by providing an `access_token`
for a server admin: see [Admin API](#admin-api-introduction).

#### Parameters

The following parameters are available:

* `user_id` - Fully qualified user: for example, `@user:server.com`.
* `room_id_or_alias` - The room identifier or alias to join: for example,
  `!636q39766251:server.com`.

#### Usage

```
POST /_synapse/admin/v1/join/<room_id_or_alias>

{
  "user_id": "@user:server.com"
}
```

Response:

```json
{
  "room_id": "!636q39766251:server.com"
}
```

---

## Rooms

*Source: [`docs/admin_api/rooms.md`](https://github.com/element-hq/synapse/blob/v1.165.0/docs/admin_api/rooms.md) · [rendered](https://element-hq.github.io/synapse/v1.165/admin_api/rooms.html)*

### List Room API

The List Room admin API allows server admins to get a list of rooms on their
server. There are various parameters available that allow for filtering and
sorting the returned list. This API supports pagination.

To use it, you will need to authenticate by providing an `access_token`
for a server admin: see [Admin API](#admin-api-introduction).

**Parameters**

The following query parameters are available:

* `from` - Offset in the returned list. Defaults to `0`.
* `limit` - Maximum amount of rooms to return. Defaults to `100`.
* `order_by` - The method in which to sort the returned list of rooms. Valid values are:
  - `alphabetical` - Same as `name`. This is deprecated.
  - `size` - Same as `joined_members`. This is deprecated.
  - `name` - Rooms are ordered alphabetically by room name. This is the default.
  - `canonical_alias` - Rooms are ordered alphabetically by main alias address of the room.
  - `joined_members` - Rooms are ordered by the number of members. Largest to smallest.
  - `joined_local_members` - Rooms are ordered by the number of local members. Largest to smallest.
  - `version` - Rooms are ordered by room version. Largest to smallest.
  - `creator` - Rooms are ordered alphabetically by creator of the room.
  - `encryption` - Rooms are ordered alphabetically by the end-to-end encryption algorithm.
  - `federatable` - Rooms are ordered by whether the room is federatable.
  - `public` - Rooms are ordered by visibility in room list.
  - `join_rules` - Rooms are ordered alphabetically by join rules of the room.
  - `guest_access` - Rooms are ordered alphabetically by guest access option of the room.
  - `history_visibility` - Rooms are ordered alphabetically by visibility of history of the room.
  - `state_events` - Rooms are ordered by number of state events. Largest to smallest.
* `dir` - Direction of room order. Either `f` for forwards or `b` for backwards. Setting
  this value to `b` will reverse the above sort order. Defaults to `f`.
* `search_term` - Filter rooms by their room name, canonical alias and room id.
  Specifically, rooms are selected if the search term is contained in
  - the room's name,
  - the local part of the room's canonical alias, or
  - the complete (local and server part) room's id (case sensitive).
* `public_rooms` - Optional flag to filter public rooms. If `true`, only public rooms are queried. If `false`, public rooms are excluded from
  the query. When the flag is absent (the default), **both** public and non-public rooms are included in the search results.
* `empty_rooms` - Optional flag to filter empty rooms. A room is empty if joined_members is zero. If `true`, only empty rooms are queried. If `false`, empty rooms are excluded from
  the query. When the flag is absent (the default), **both** empty and non-empty rooms are included in the search results.

  Defaults to no filtering.

**Response**

The following fields are possible in the JSON response body:

* `rooms` - An array of objects, each containing information about a room.
  - Room objects contain the following fields:
    - `room_id` - The ID of the room.
    - `name` - The name of the room.
    - `canonical_alias` - The canonical (main) alias address of the room.
    - `joined_members` - How many users are currently in the room.
    - `joined_local_members` - How many local users are currently in the room.
    - `version` - The version of the room as a string.
    - `creator` - The `user_id` of the room creator.
    - `encryption` - Algorithm of end-to-end encryption of messages. Is `null` if encryption is not active.
    - `federatable` - Whether users on other servers can join this room.
    - `public` - Whether the room is visible in room directory.
    - `join_rules` - The type of rules used for users wishing to join this room. One of: ["public", "knock", "invite", "private"].
    - `guest_access` - Whether guests can join the room. One of: ["can_join", "forbidden"].
    - `history_visibility` - Who can see the room history. One of: ["invited", "joined", "shared", "world_readable"].
    - `state_events` - Total number of state_events of a room. Complexity of the room.
    - `room_type` - The type of the room taken from the room's creation event; for example "m.space" if the room is a space. If the room does not define a type, the value will be `null`.
* `offset` - The current pagination offset in rooms. This parameter should be
             used instead of `next_token` for room offset as `next_token` is
             not intended to be parsed.
* `total_rooms` - The total number of rooms this query can return. Using this
                  and `offset`, you have enough information to know the current
                  progression through the list.
* `next_batch` - If this field is present, we know that there are potentially
                 more rooms on the server that did not all fit into this response.
                 We can use `next_batch` to get the "next page" of results. To do
                 so, simply repeat your request, setting the `from` parameter to
                 the value of `next_batch`.
* `prev_batch` - If this field is present, it is possible to paginate backwards.
                 Use `prev_batch` for the `from` value in the next request to
                 get the "previous page" of results.

The API is:

A standard request with no filtering:

```
GET /_synapse/admin/v1/rooms
```

A response body like the following is returned:

```json
{
  "rooms": [
    {
      "room_id": "!OGEhHVWSdvArJzumhm:matrix.org",
      "name": "Matrix HQ",
      "canonical_alias": "#matrix:matrix.org",
      "joined_members": 8326,
      "joined_local_members": 2,
      "version": "1",
      "creator": "@foo:matrix.org",
      "encryption": null,
      "federatable": true,
      "public": true,
      "join_rules": "invite",
      "guest_access": null,
      "history_visibility": "shared",
      "state_events": 93534,
      "room_type": "m.space"
    },
    ... (8 hidden items) ...
    {
      "room_id": "!xYvNcQPhnkrdUmYczI:matrix.org",
      "name": "This Week In Matrix (TWIM)",
      "canonical_alias": "#twim:matrix.org",
      "joined_members": 314,
      "joined_local_members": 20,
      "version": "4",
      "creator": "@foo:matrix.org",
      "encryption": "m.megolm.v1.aes-sha2",
      "federatable": true,
      "public": false,
      "join_rules": "invite",
      "guest_access": null,
      "history_visibility": "shared",
      "state_events": 8345,
      "room_type": null
    }
  ],
  "offset": 0,
  "total_rooms": 10
}
```

Filtering by room name:

```
GET /_synapse/admin/v1/rooms?search_term=TWIM
```

A response body like the following is returned:

```json
{
  "rooms": [
    {
      "room_id": "!xYvNcQPhnkrdUmYczI:matrix.org",
      "name": "This Week In Matrix (TWIM)",
      "canonical_alias": "#twim:matrix.org",
      "joined_members": 314,
      "joined_local_members": 20,
      "version": "4",
      "creator": "@foo:matrix.org",
      "encryption": "m.megolm.v1.aes-sha2",
      "federatable": true,
      "public": false,
      "join_rules": "invite",
      "guest_access": null,
      "history_visibility": "shared",
      "state_events": 8,
      "room_type": null
    }
  ],
  "offset": 0,
  "total_rooms": 1
}
```

Paginating through a list of rooms:

```
GET /_synapse/admin/v1/rooms?order_by=size
```

A response body like the following is returned:

```json
{
  "rooms": [
    {
      "room_id": "!OGEhHVWSdvArJzumhm:matrix.org",
      "name": "Matrix HQ",
      "canonical_alias": "#matrix:matrix.org",
      "joined_members": 8326,
      "joined_local_members": 2,
      "version": "1",
      "creator": "@foo:matrix.org",
      "encryption": null,
      "federatable": true,
      "public": true,
      "join_rules": "invite",
      "guest_access": null,
      "history_visibility": "shared",
      "state_events": 93534,
      "room_type": null
    },
    ... (98 hidden items) ...
    {
      "room_id": "!xYvNcQPhnkrdUmYczI:matrix.org",
      "name": "This Week In Matrix (TWIM)",
      "canonical_alias": "#twim:matrix.org",
      "joined_members": 314,
      "joined_local_members": 20,
      "version": "4",
      "creator": "@foo:matrix.org",
      "encryption": "m.megolm.v1.aes-sha2",
      "federatable": true,
      "public": false,
      "join_rules": "invite",
      "guest_access": null,
      "history_visibility": "shared",
      "state_events": 8345,
      "room_type": "m.space"
    }
  ],
  "offset": 0,
  "total_rooms": 150,
  "next_token": 100
}
```

The presence of the `next_token` parameter tells us that there are more rooms
than returned in this request, and we need to make another request to get them.
To get the next batch of room results, we repeat our request, setting the `from`
parameter to the value of `next_token`.

```
GET /_synapse/admin/v1/rooms?order_by=size&from=100
```

A response body like the following is returned:

```json
{
  "rooms": [
    {
      "room_id": "!mscvqgqpHYjBGDxNym:matrix.org",
      "name": "Music Theory",
      "canonical_alias": "#musictheory:matrix.org",
      "joined_members": 127,
      "joined_local_members": 2,
      "version": "1",
      "creator": "@foo:matrix.org",
      "encryption": null,
      "federatable": true,
      "public": true,
      "join_rules": "invite",
      "guest_access": null,
      "history_visibility": "shared",
      "state_events": 93534,
      "room_type": "m.space"

    },
    ... (48 hidden items) ...
    {
      "room_id": "!twcBhHVdZlQWuuxBhN:termina.org.uk",
      "name": "weechat-matrix",
      "canonical_alias": "#weechat-matrix:termina.org.uk",
      "joined_members": 137,
      "joined_local_members": 20,
      "version": "4",
      "creator": "@foo:termina.org.uk",
      "encryption": null,
      "federatable": true,
      "public": true,
      "join_rules": "invite",
      "guest_access": null,
      "history_visibility": "shared",
      "state_events": 8345,
      "room_type": null

    }
  ],
  "offset": 100,
  "prev_batch": 0,
  "total_rooms": 150
}
```

Once the `next_token` parameter is no longer present, we know we've reached the
end of the list.

### Room Details API

The Room Details admin API allows server admins to get all details of a room.

The following fields are possible in the JSON response body:

* `room_id` - The ID of the room.
* `name` - The name of the room.
* `topic` - The topic of the room.
* `avatar` - The `mxc` URI to the avatar of the room.
* `canonical_alias` - The canonical (main) alias address of the room.
* `joined_members` - How many users are currently in the room.
* `joined_local_members` - How many local users are currently in the room.
* `joined_local_devices` - How many local devices are currently in the room.
* `version` - The version of the room as a string.
* `creator` - The `user_id` of the room creator.
* `encryption` - Algorithm of end-to-end encryption of messages. Is `null` if encryption is not active.
* `federatable` - Whether users on other servers can join this room.
* `public` - Whether the room is visible in room directory.
* `join_rules` - The type of rules used for users wishing to join this room. One of: ["public", "knock", "invite", "private"].
* `guest_access` - Whether guests can join the room. One of: ["can_join", "forbidden"].
* `history_visibility` - Who can see the room history. One of: ["invited", "joined", "shared", "world_readable"].
* `state_events` - Total number of state_events of a room. Complexity of the room.
* `room_type` - The type of the room taken from the room's creation event; for example "m.space" if the room is a space.
  If the room does not define a type, the value will be `null`.
* `forgotten` - Whether all local users have
  [forgotten](https://spec.matrix.org/latest/client-server-api/#leaving-rooms) the room.
* `tombstoned` - Whether the room has been tombstoned (permanently closed).
* `replacement_room` - The room ID of the new room that users should join instead, if this room was tombstoned. Will be 
  `null` if the room has not been tombstoned, or if it was tombstoned without designating a successor room.

The API is:

```
GET /_synapse/admin/v1/rooms/<room_id>
```

A response body like the following is returned:

```json
{
  "room_id": "!mscvqgqpHYjBGDxNym:matrix.org",
  "name": "Music Theory",
  "avatar": "mxc://matrix.org/AQDaVFlbkQoErdOgqWRgiGSV",
  "topic": "Theory, Composition, Notation, Analysis",
  "canonical_alias": "#musictheory:matrix.org",
  "joined_members": 127,
  "joined_local_members": 2,
  "joined_local_devices": 2,
  "version": "1",
  "creator": "@foo:matrix.org",
  "encryption": null,
  "federatable": true,
  "public": true,
  "join_rules": "invite",
  "guest_access": null,
  "history_visibility": "shared",
  "state_events": 93534,
  "room_type": "m.space",
  "forgotten": false,
  "tombstoned": false,
  "replacement_room": null
}
```

_Changed in Synapse 1.66:_ Added the `forgotten` key to the response body.

### Room Members API

The Room Members admin API allows server admins to get a list of all members of a room.

The response includes the following fields:

* `members` - A list of all the members that are present in the room, represented by their ids.
* `total` - Total number of members in the room.

The API is:

```
GET /_synapse/admin/v1/rooms/<room_id>/members
```

A response body like the following is returned:

```json
{
  "members": [
    "@foo:matrix.org",
    "@bar:matrix.org",
    "@foobar:matrix.org"
  ],
  "total": 3
}
```

### Room State API

The Room State admin API allows server admins to get a list of all state events in a room.

The response includes the following fields:

* `state` - The current state of the room at the time of request.

The API is:

```
GET /_synapse/admin/v1/rooms/<room_id>/state
```

**Parameters**

The following query parameter is available:

* `type` - The type of room state event to filter by, eg "m.room.create". If provided, only state events
    of this type will be returned (regardless of their `state_key` value).

A response body like the following is returned:

```json
{
  "state": [
    {"type": "m.room.create", "state_key": "", "etc": true},
    {"type": "m.room.power_levels", "state_key": "", "etc": true},
    {"type": "m.room.name", "state_key": "", "etc": true}
  ]
}
```

### Room Messages API

The Room Messages admin API allows server admins to get all messages
sent to a room in a given timeframe. There are various parameters available
that allow for filtering and ordering the returned list. This API supports pagination.

To use it, you will need to authenticate by providing an `access_token`
for a server admin: see [Admin API](#admin-api-introduction).

This endpoint mirrors the [Matrix Spec defined Messages API](https://spec.matrix.org/v1.1/client-server-api/#get_matrixclientv3roomsroomidmessages).

The API is:
```
GET /_synapse/admin/v1/rooms/<room_id>/messages
```

**Parameters**

The following path parameters are required:

* `room_id` - The ID of the room you wish you fetch messages from.

The following query parameters are available:

* `from` (required) - The token to start returning events from. This token can be obtained from a prev_batch
  or next_batch token returned by the /sync endpoint, or from an end token returned by a previous request to this endpoint.
* `to` - The token to stop returning events at.
* `limit` - The maximum number of events to return. Defaults to `10`.
* `filter` - A JSON RoomEventFilter to filter returned events with.
* `dir` - The direction to return events from. Either `f` for forwards or `b` for backwards. Setting
  this value to `b` will reverse the above sort order. Defaults to `f`.

**Response**

The following fields are possible in the JSON response body:

* `chunk` - A list of room events. The order depends on the dir parameter.
          Note that an empty chunk does not necessarily imply that no more events are available. Clients should continue to paginate until no end property is returned.
* `end` - A token corresponding to the end of chunk. This token can be passed back to this endpoint to request further events.
          If no further events are available, this property is omitted from the response.
* `start` - A token corresponding to the start of chunk.
* `state` - A list of state events relevant to showing the chunk.

**Example**

For more details on each chunk, read [the Matrix specification](https://spec.matrix.org/v1.1/client-server-api/#get_matrixclientv3roomsroomidmessages).

```json
{
  "chunk": [
    {
      "content": {
        "body": "This is an example text message",
        "format": "org.matrix.custom.html",
        "formatted_body": "<b>This is an example text message</b>",
        "msgtype": "m.text"
      },
      "event_id": "$143273582443PhrSn:example.org",
      "origin_server_ts": 1432735824653,
      "room_id": "!636q39766251:example.com",
      "sender": "@example:example.org",
      "type": "m.room.message",
      "unsigned": {
        "age": 1234
      }
    },
    {
      "content": {
        "name": "The room name"
      },
      "event_id": "$143273582443PhrSn:example.org",
      "origin_server_ts": 1432735824653,
      "room_id": "!636q39766251:example.com",
      "sender": "@example:example.org",
      "state_key": "",
      "type": "m.room.name",
      "unsigned": {
        "age": 1234
      }
    },
    {
      "content": {
        "body": "Gangnam Style",
        "info": {
          "duration": 2140786,
          "h": 320,
          "mimetype": "video/mp4",
          "size": 1563685,
          "thumbnail_info": {
            "h": 300,
            "mimetype": "image/jpeg",
            "size": 46144,
            "w": 300
          },
          "thumbnail_url": "mxc://example.org/FHyPlCeYUSFFxlgbQYZmoEoe",
          "w": 480
        },
        "msgtype": "m.video",
        "url": "mxc://example.org/a526eYUSFFxlgbQYZmo442"
      },
      "event_id": "$143273582443PhrSn:example.org",
      "origin_server_ts": 1432735824653,
      "room_id": "!636q39766251:example.com",
      "sender": "@example:example.org",
      "type": "m.room.message",
      "unsigned": {
        "age": 1234
      }
    }
  ],
  "end": "t47409-4357353_219380_26003_2265",
  "start": "t47429-4392820_219380_26003_2265"
}
```

### Room Timestamp to Event API

The Room Timestamp to Event API endpoint fetches the `event_id` of the closest event to the given
timestamp (`ts` query parameter) in the given direction (`dir` query parameter).

Useful for cases like jump to date so you can start paginating messages from
a given date in the archive.

The API is:
```
  GET /_synapse/admin/v1/rooms/<room_id>/timestamp_to_event
```

**Parameters**

The following path parameters are required:

* `room_id` - The ID of the room you wish to check.

The following query parameters are available:

* `ts` - a timestamp in milliseconds where we will find the closest event in
  the given direction.
* `dir` - can be `f` or `b` to indicate forwards and backwards in time from the
  given timestamp. Defaults to `f`.

**Response**

* `event_id` - The event ID closest to the given timestamp.
* `origin_server_ts` - The timestamp of the event in milliseconds since the Unix epoch.

### Block Room API
The Block Room admin API allows server admins to block and unblock rooms,
and query to see if a given room is blocked.
This API can be used to pre-emptively block a room, even if it's unknown to this
homeserver. Users will be prevented from joining a blocked room.

#### Block or unblock a room

The API is:

```
PUT /_synapse/admin/v1/rooms/<room_id>/block
```

with a body of:

```json
{
    "block": true
}
```

A response body like the following is returned:

```json
{
    "block": true
}
```

**Parameters**

The following parameters should be set in the URL:

- `room_id` - The ID of the room.

The following JSON body parameters are available:

- `block` - If `true` the room will be blocked and if `false` the room will be unblocked.

**Response**

The following fields are possible in the JSON response body:

- `block` - A boolean. `true` if the room is blocked, otherwise `false`

#### Get block status

The API is:

```
GET /_synapse/admin/v1/rooms/<room_id>/block
```

A response body like the following is returned:

```json
{
    "block": true,
    "user_id": "<user_id>"
}
```

**Parameters**

The following parameters should be set in the URL:

- `room_id` - The ID of the room.

**Response**

The following fields are possible in the JSON response body:

- `block` - A boolean. `true` if the room is blocked, otherwise `false`
- `user_id` - An optional string. If the room is blocked (`block` is `true`) shows
  the user who has add the room to blocking list. Otherwise it is not displayed.

### Delete Room API

The Delete Room admin API allows server admins to remove rooms from the server
and block these rooms.

Shuts down a room. Moves all local users and room aliases automatically to a
new room if `new_room_user_id` is set. Otherwise local users only
leave the room without any information.

The new room will be created with the user specified by the `new_room_user_id` parameter
as room administrator and will contain a message explaining what happened. Users invited
to the new room will have power level `-10` by default, and thus be unable to speak.

If `block` is `true`, users will be prevented from joining the old room.
This option can in [Version 1](#version-1-old-version) also be used to pre-emptively
block a room, even if it's unknown to this homeserver. In this case, the room will be
blocked, and no further action will be taken. If `block` is `false`, attempting to
delete an unknown room is invalid and will be rejected as a bad request.

This API will remove all trace of the old room from your database after removing
all local users. If `purge` is `true` (the default), all traces of the old room will
be removed from your database after removing all local users. If you do not want
this to happen, set `purge` to `false`.
Depending on the amount of history being purged, a call to the API may take
several minutes or longer.

The local server will only have the power to move local user and room aliases to
the new room. Users on other servers will be unaffected.

#### Version 1 (old version)

This version works synchronously. That means you only get the response once the server has
finished the action, which may take a long time. If you request the same action
a second time, and the server has not finished the first one, the second request will block.
This is fixed in version 2 of this API. The parameters are the same in both APIs.
This API will become deprecated in the future.

The API is:

```
DELETE /_synapse/admin/v1/rooms/<room_id>
```

with a body of:

```json
{
    "new_room_user_id": "@someuser:example.com",
    "room_name": "Content Violation Notification",
    "message": "Bad Room has been shutdown due to content violations on this server. Please review our Terms of Service.",
    "block": true,
    "purge": true
}
```

A response body like the following is returned:

```json
{
    "kicked_users": [
        "@foobar:example.com"
    ],
    "failed_to_kick_users": [],
    "local_aliases": [
        "#badroom:example.com",
        "#evilsaloon:example.com"
    ],
    "new_room_id": "!newroomid:example.com"
}
```

The parameters and response values have the same format as
[version 2](#version-2-new-version) of the API.

#### Version 2 (new version)

**Note**: This API is new, experimental and "subject to change".

This version works asynchronously, meaning you get the response from server immediately
while the server works on that task in background. You can then request the status of the action
to check if it has completed.

The API is:

```
DELETE /_synapse/admin/v2/rooms/<room_id>
```

with a body of:

```json
{
    "new_room_user_id": "@someuser:example.com",
    "room_name": "Content Violation Notification",
    "message": "Bad Room has been shutdown due to content violations on this server. Please review our Terms of Service.",
    "block": true,
    "purge": true
}
```

The API starts the shut down and purge running, and returns immediately with a JSON body with
a purge id:

```json
{
    "delete_id": "<opaque id>"
}
```

**Parameters**

The following parameters should be set in the URL:

* `room_id` - The ID of the room.

The following JSON body parameters are available:

* `new_room_user_id` - Optional. If set, a new room will be created with this user ID
      as the creator and admin, and all users in the old room will be moved into that
      room. If not set, no new room will be created and the users will just be removed
      from the old room. The user ID must be on the local server, but does not necessarily
      have to belong to a registered user.
* `room_name` - Optional. A string representing the name of the room that new users will be
                invited to. Defaults to `Content Violation Notification`
* `message` - Optional. A string containing the first message that will be sent as
              `new_room_user_id` in the new room. Ideally this will clearly convey why the
               original room was shut down. Defaults to `Sharing illegal content on this server
               is not permitted and rooms in violation will be blocked.`
* `block` - Optional. If set to `true`, this room will be added to a blocking list,
            preventing future attempts to join the room. Rooms can be blocked
            even if they're not yet known to the homeserver (only with
            [Version 1](#version-1-old-version) of the API). Defaults to `false`.
* `purge` - Optional. If set to `true`, it will remove all traces of the room from your database.
            Defaults to `true`.
* `force_purge` - Optional, and ignored unless `purge` is `true`. If set to `true`, it
  will force a purge to go ahead even if there are local users still in the room. Do not
  use this unless a regular `purge` operation fails, as it could leave those users'
  clients in a confused state.

The JSON body must not be empty. The body must be at least `{}`.

#### Status of deleting rooms

**Note**: This API is new, experimental and "subject to change".

It is possible to query the status of the background task for deleting rooms.
The status can be queried up to 24 hours after completion of the task,
or until Synapse is restarted (whichever happens first).

##### Query by `room_id`

With this API you can get the status of all active deletion tasks, and all those completed in the last 24h,
for the given `room_id`.

The API is:

```
GET /_synapse/admin/v2/rooms/<room_id>/delete_status
```

A response body like the following is returned:

```json
{
    "results": [
        {
            "delete_id": "delete_id1",
            "room_id": "!roomid:example.com",
            "status": "failed",
            "error": "error message",
            "shutdown_room": {
                "kicked_users": [],
                "failed_to_kick_users": [],
                "local_aliases": [],
                "new_room_id": null
            }
        }, {
            "delete_id": "delete_id2",
            "room_id": "!roomid:example.com",
            "status": "active",
            "shutdown_room": {
                "kicked_users": [
                    "@foobar:example.com"
                ],
                "failed_to_kick_users": [],
                "local_aliases": [
                    "#badroom:example.com",
                    "#evilsaloon:example.com"
                ],
                "new_room_id": "!newroomid:example.com"
            }
        }
    ]
}
```

**Parameters**

The following parameters should be set in the URL:

* `room_id` - The ID of the room.

##### Query by `delete_id`

With this API you can get the status of one specific task by `delete_id`.

The API is:

```
GET /_synapse/admin/v2/rooms/delete_status/<delete_id>
```

A response body like the following is returned:

```json
{
    "status": "active",
    "delete_id": "bHkCNQpHqOaFhPtK",
    "room_id": "!roomid:example.com",
    "shutdown_room": {
        "kicked_users": [
            "@foobar:example.com"
        ],
        "failed_to_kick_users": [],
        "local_aliases": [
            "#badroom:example.com",
            "#evilsaloon:example.com"
        ],
        "new_room_id": "!newroomid:example.com"
    }
}
```

**Parameters**

The following parameters should be set in the URL:

* `delete_id` - The ID for this delete.

##### Response

The following fields are returned in the JSON response body:

- `results` - An array of objects, each containing information about one task.
  This field is omitted from the result when you query by `delete_id`.
  Task objects contain the following fields:
  - `delete_id` - The ID for this purge
  - `room_id` - The ID of the room being deleted
  - `status` - The status will be one of:
    - `scheduled` - The deletion is waiting to be started
    - `active` - The process is purging the room and event data from database.
    - `complete` - The process has completed successfully.
    - `failed` - The process is aborted, an error has occurred.
  - `error` - A string that shows an error message if `status` is `failed`.
    Otherwise this field is hidden.
  - `shutdown_room` - An object containing information about the result of shutting down the room.
    *Note:* The result is shown after removing the room members.
    The delete process can still be running. Please pay attention to the `status`.
    - `kicked_users` - An array of users (`user_id`) that were kicked.
    - `failed_to_kick_users` - An array of users (`user_id`) that that were not kicked.
    - `local_aliases` - An array of strings representing the local aliases that were
      migrated from the old room to the new.
    - `new_room_id` - A string representing the room ID of the new room, or `null` if
      no such room was created.

#### Undoing room deletions

*Note*: This guide may be outdated by the time you read it. By nature of room deletions being performed at the database level,
the structure can and does change without notice.

First, it's important to understand that a room deletion is very destructive. Undoing a deletion is not as simple as pretending it
never happened - work has to be done to move forward instead of resetting the past. In fact, in some cases it might not be possible
to recover at all:

* If the room was invite-only, your users will need to be re-invited.
* If the room no longer has any members at all, it'll be impossible to rejoin.
* The first user to rejoin will have to do so via an alias on a different
  server (or receive an invite from a user on a different server).

With all that being said, if you still want to try and recover the room:

1. If the room was `block`ed, you must unblock it on your server. This can be
   accomplished as follows:

   1. For safety reasons, shut down Synapse.
   2. In the database, run `DELETE FROM blocked_rooms WHERE room_id = '!example:example.org';`
      * For caution: it's recommended to run this in a transaction: `BEGIN; DELETE ...;`, verify you got 1 result, then `COMMIT;`.
      * The room ID is the same one supplied to the delete room API, not the Content Violation room.
   3. Restart Synapse.

   This step is unnecessary if `block` was not set.

2. Any room aliases on your server that pointed to the deleted room may have
   been deleted, or redirected to the Content Violation room. These will need
   to be restored manually.

3. Users on your server that were in the deleted room will have been kicked
   from the room. Consider whether you want to update their membership
   (possibly via the [Edit Room Membership API](#manipulate-room-membership)) or let
   them handle rejoining themselves.

4. If `new_room_user_id` was given, a 'Content Violation' will have been
   created. Consider whether you want to delete that room.

### Make Room Admin API

Grants another user the highest power available to a local user who is in the room.
If the user is not in the room, and it is not publicly joinable, then invite the user.

By default the server admin (the caller) is granted power, but another user can
optionally be specified, e.g.:

```
POST /_synapse/admin/v1/rooms/<room_id_or_alias>/make_room_admin
{
    "user_id": "@foo:example.com"
}
```

### Forward Extremities Admin API

Enables querying and deleting forward extremities from rooms. When a lot of forward
extremities accumulate in a room, performance can become degraded. For details, see
[#1760](https://github.com/matrix-org/synapse/issues/1760).

#### Check for forward extremities

To check the status of forward extremities for a room:

```
GET /_synapse/admin/v1/rooms/<room_id_or_alias>/forward_extremities
```

A response as follows will be returned:

```json
{
  "count": 1,
  "results": [
    {
      "event_id": "$M5SP266vsnxctfwFgFLNceaCo3ujhRtg_NiiHabcdefgh",
      "state_group": 439,
      "depth": 123,
      "received_ts": 1611263016761
    }
  ]
}
```

#### Deleting forward extremities

**WARNING**: Please ensure you know what you're doing and have read
the related issue [#1760](https://github.com/matrix-org/synapse/issues/1760).
Under no situations should this API be executed as an automated maintenance task!

If a room has lots of forward extremities, the extra can be
deleted as follows:

```
DELETE /_synapse/admin/v1/rooms/<room_id_or_alias>/forward_extremities
```

A response as follows will be returned, indicating the amount of forward extremities
that were deleted.

```json
{
  "deleted": 1
}
```

### Event Context API

This API lets a client find the context of an event. This is designed primarily to investigate abuse reports.

```
GET /_synapse/admin/v1/rooms/<room_id>/context/<event_id>
```

This API mimmicks [GET /_matrix/client/r0/rooms/{roomId}/context/{eventId}](https://matrix.org/docs/spec/client_server/r0.6.1#get-matrix-client-r0-rooms-roomid-context-eventid). Please refer to the link for all details on parameters and reseponse.

Example response:

```json
{
  "end": "t29-57_2_0_2",
  "events_after": [
    {
      "content": {
        "body": "This is an example text message",
        "msgtype": "m.text",
        "format": "org.matrix.custom.html",
        "formatted_body": "<b>This is an example text message</b>"
      },
      "type": "m.room.message",
      "event_id": "$143273582443PhrSn:example.org",
      "room_id": "!636q39766251:example.com",
      "sender": "@example:example.org",
      "origin_server_ts": 1432735824653,
      "unsigned": {
        "age": 1234
      }
    }
  ],
  "event": {
    "content": {
      "body": "filename.jpg",
      "info": {
        "h": 398,
        "w": 394,
        "mimetype": "image/jpeg",
        "size": 31037
      },
      "url": "mxc://example.org/JWEIFJgwEIhweiWJE",
      "msgtype": "m.image"
    },
    "type": "m.room.message",
    "event_id": "$f3h4d129462ha:example.com",
    "room_id": "!636q39766251:example.com",
    "sender": "@example:example.org",
    "origin_server_ts": 1432735824653,
    "unsigned": {
      "age": 1234
    }
  },
  "events_before": [
    {
      "content": {
        "body": "something-important.doc",
        "filename": "something-important.doc",
        "info": {
          "mimetype": "application/msword",
          "size": 46144
        },
        "msgtype": "m.file",
        "url": "mxc://example.org/FHyPlCeYUSFFxlgbQYZmoEoe"
      },
      "type": "m.room.message",
      "event_id": "$143273582443PhrSn:example.org",
      "room_id": "!636q39766251:example.com",
      "sender": "@example:example.org",
      "origin_server_ts": 1432735824653,
      "unsigned": {
        "age": 1234
      }
    }
  ],
  "start": "t27-54_2_0_2",
  "state": [
    {
      "content": {
        "creator": "@example:example.org",
        "room_version": "1",
        "m.federate": true,
        "predecessor": {
          "event_id": "$something:example.org",
          "room_id": "!oldroom:example.org"
        }
      },
      "type": "m.room.create",
      "event_id": "$143273582443PhrSn:example.org",
      "room_id": "!636q39766251:example.com",
      "sender": "@example:example.org",
      "origin_server_ts": 1432735824653,
      "unsigned": {
        "age": 1234
      },
      "state_key": ""
    },
    {
      "content": {
        "membership": "join",
        "avatar_url": "mxc://example.org/SEsfnsuifSDFSSEF",
        "displayname": "Alice Margatroid"
      },
      "type": "m.room.member",
      "event_id": "$143273582443PhrSn:example.org",
      "room_id": "!636q39766251:example.com",
      "sender": "@example:example.org",
      "origin_server_ts": 1432735824653,
      "unsigned": {
        "age": 1234
      },
      "state_key": "@alice:example.org"
    }
  ]
}
```

### Admin Space Hierarchy Endpoint

This API allows an admin to fetch the space/room hierarchy for a given space, 
returning details about that room and any children the room may have, paginating
over the space tree in a depth-first manner to locate child rooms. This is
functionally similar to the [CS Hierarchy](https://spec.matrix.org/v1.16/client-server-api/#get_matrixclientv1roomsroomidhierarchy) endpoint but does not check for
room membership when returning room summaries.

The endpoint does not query other servers over federation about remote rooms
that the server has not joined. This is a deliberate trade-off: while this
means it will leave some holes in the hierarchy that we could otherwise
sometimes fill in, it significantly improves the endpoint's response time and
the admin endpoint is designed for managing rooms local to the homeserver
anyway.

**Parameters**

The following query parameters are available:

* `from` - An optional pagination token, provided when there are more rooms to 
    return than the limit. 
* `limit` - Maximum amount of rooms to return. Must be a non-negative integer,
   defaults to `50`.
* `max_depth` - The maximum depth in the tree to explore, must be a non-negative
   integer. 0 would correspond to just the root room, 1 would include just the
   root room's children, etc.  If not provided will recurse into the space tree without limit.

Request:

```http
GET /_synapse/admin/v1/rooms/<room_id>/hierarchy
```

Response:

```json
{
  "rooms":
      [
        { "children_state": [
            {
              "content": {
                "via": ["local_test_server"]
              },
              "origin_server_ts": 1500,
              "sender": "@user:test",
              "state_key": "!QrMkkqBSwYRIFNFCso:test",
              "type": "m.space.child"
            }
        ],
        "name": "space room",
        "guest_can_join": false,
        "join_rule": "public",
        "num_joined_members": 1,
        "room_id": "!sPOpNyMHbZAoAOsOFL:test",
        "room_type": "m.space",
        "world_readable": false
      },

      {
        "children_state": [],
        "guest_can_join": true,
        "join_rule": "invite",
        "name": "nefarious",
        "num_joined_members": 1,
        "room_id": "!QrMkkqBSwYRIFNFCso:test",
        "topic": "being bad",
        "world_readable": false}
    ],
  "next_batch": "KUYmRbeSpAoaAIgOKGgyaCEn"
}
```

---

## Scheduled tasks

*Source: [`docs/admin_api/scheduled_tasks.md`](https://github.com/element-hq/synapse/blob/v1.165.0/docs/admin_api/scheduled_tasks.md) · [rendered](https://element-hq.github.io/synapse/v1.165/admin_api/scheduled_tasks.html)*

### Show scheduled tasks

This API returns information about scheduled tasks.

To use it, you will need to authenticate by providing an `access_token`
for a server admin: see [Admin API](#admin-api-introduction).

The api is:
```
GET /_synapse/admin/v1/scheduled_tasks
```

It returns a JSON body like the following:

```json
{
    "scheduled_tasks": [
        {
            "id": "GSA124oegf1",
            "action": "shutdown_room",
            "status": "complete",
            "timestamp_ms": 23423523,
            "resource_id": "!roomid",
            "result": "some result",
            "error": null
        }
      ]
}
```

**Query parameters:**

* `action_name`: string - Is optional. Returns only the scheduled tasks with the given action name.
  May be given multiple times to return tasks matching any of the given action names.
* `resource_id`: string - Is optional. Returns only the scheduled tasks with the given resource id.
* `status`: string - Is optional. Returns only the scheduled tasks matching the given status.
  May be given multiple times to return tasks matching any of the given statuses. The status must be one of
    - "scheduled" - Task is scheduled but not active
    - "active" - Task is active and probably running, and if not will be run on next scheduler loop run
    - "complete" - Task has completed successfully
    - "cancelled" - Task has been cancelled
    - "failed" - Task is over and either returned a failed status, or had an exception

* `max_timestamp`: int - Is optional. Returns only the scheduled tasks with a timestamp (in milliseconds since the unix epoch) inferior to the specified one.

**Response**

The following fields are returned in the JSON response body along with a `200` HTTP status code:

* `id`: string - ID of scheduled task.
* `action`: string - The name of the scheduled task's action.
* `status`: string - The status of the scheduled task.
* `timestamp_ms`: integer - The timestamp (in milliseconds since the unix epoch) of the given task - If the status is "scheduled" then this represents when it should be launched.
  Otherwise it represents the last time this task got a change of state.
* `resource_id`: Optional string - The resource id of the scheduled task, if it possesses one
* `result`: Optional Json - Any result of the scheduled task, if given
* `error`: Optional string - If the task has the status "failed", the error associated with this failure

---

## Server Notices

*Source: [`docs/admin_api/server_notices.md`](https://github.com/element-hq/synapse/blob/v1.165.0/docs/admin_api/server_notices.md) · [rendered](https://element-hq.github.io/synapse/v1.165/admin_api/server_notices.html)*

### Server Notices

The API to send notices is as follows:

```
POST /_synapse/admin/v1/send_server_notice
```

or:

```
PUT /_synapse/admin/v1/send_server_notice/{txnId}
```

You will need to authenticate with an access token for an admin user.

When using the `PUT` form, retransmissions with the same transaction ID will be
ignored in the same way as with `PUT
/_matrix/client/r0/rooms/{roomId}/send/{eventType}/{txnId}`.

The request body should look something like the following:

```json
{
    "user_id": "@target_user:server_name",
    "content": {
        "msgtype": "m.text",
        "body": "This is my message"
    }
}
```

You can optionally include the following additional parameters:

* `type`: the type of event. Defaults to `m.room.message`.
* `state_key`: Setting this will result in a state event being sent.


Once the notice has been sent, the API will return the following response:

```json
{
    "event_id": "<event_id>"
}
```

Note that server notices must be enabled in `homeserver.yaml` before this API
can be used. See [the server notices documentation](https://element-hq.github.io/synapse/v1.165/server_notices.html) for more information.

---

## Server Announcements

*Source: [`docs/admin_api/server_announcements.md`](https://github.com/element-hq/synapse/blob/v1.165.0/docs/admin_api/server_announcements.md) · [rendered](https://element-hq.github.io/synapse/v1.165/admin_api/server_announcements.html)*

### Server Announcements

These APIs read and send to the server announcement rooms configured under
`experimental_features.server_announcements.rooms` in `homeserver.yaml`. The
feature is experimental; see the
[configuration module](https://github.com/element-hq/synapse/blob/develop/synapse/config/server_announcements.py)
for how to configure the rooms.

Each room is identified by the `id` of its configuration entry, not by its
Matrix room ID.

You will need to
[authenticate with an access token](https://element-hq.github.io/synapse/latest/usage/administration/admin_api/#authenticate-as-a-server-admin)
for an admin user.

#### List announcement rooms

```
GET /_synapse/admin/v1/server_announcements
```

Returns the configured announcement rooms:

```json
{
    "management_user_id": "@_server_announcements:example.com",
    "rooms": [
        {
            "id": "general",
            "room_id": "!abc:example.com",
            "room_name": "Server announcements",
            "room_topic": "Planned maintenance and incidents",
            "room_avatar_url": "mxc://example.com/xyz",
            "allowed_user_ids": ["@comms:example.com"],
            "excluded_user_ids": []
        }
    ]
}
```

* `management_user_id`: the user that creates and manages the rooms, and sends
  the announcements posted through this API.
* `rooms`: one entry per configured room, in configuration order. Empty if no
  rooms are configured.
  * `id`: the `id` of the configuration entry.
  * `room_id`: the Matrix room ID. `null` if the room has not been created,
    which happens after the room is purged while Synapse is running. Synapse
    creates it again on the next restart.
  * `room_name`, `room_topic`, `room_avatar_url`: the values from the
    configuration, which Synapse applies to the room at startup. Use the
    [room details API](#room-details-api) for the room's current state.
  * `allowed_user_ids`, `excluded_user_ids`: as configured.

Rooms whose configuration entry was removed are not listed.

#### Get an announcement room

```
GET /_synapse/admin/v1/server_announcements/{id}
```

Returns a single entry of `rooms` above, or a 404 with `M_NOT_FOUND` if `id` is
not in the configuration.

#### Send an announcement

```
POST /_synapse/admin/v1/server_announcements/{id}/send
```

or:

```
PUT /_synapse/admin/v1/server_announcements/{id}/send/{txnId}
```

Sends an event into the announcement room with configuration `id`, as the
management user. The event reaches every user that the room is delivered to.
No rate limit applies.

When using the `PUT` form, retransmissions with the same transaction ID will be
ignored in the same way as with `PUT
/_matrix/client/v3/rooms/{roomId}/send/{eventType}/{txnId}`. When Synapse
delegates authentication to the Matrix Authentication Service, the `PUT` form
needs an access token with a device.

The request body should look something like the following:

```json
{
    "content": {
        "msgtype": "m.text",
        "body": "Maintenance tonight at 22:00 UTC"
    }
}
```

You can optionally include the following additional parameter:

* `type`: the type of event. Defaults to `m.room.message`. Any non-state event
  type except `m.room.member` is accepted, so an announcement can be edited by
  sending an `m.room.message` with `m.new_content` and `m.relates_to`. To
  retract an announcement, send an `m.room.redaction` with the ID of the event
  to redact in `content.redacts`, whatever the room version. Synapse moves it
  to the top level of the event for rooms before version 11:

  ```json
  {
      "type": "m.room.redaction",
      "content": {"redacts": "$event_id"}
  }
  ```

State events cannot be sent: Synapse manages the room state from the
configuration, and a request with a `state_key` is rejected with
`M_INVALID_PARAM`.

Once the event has been sent, the API will return the following response:

```json
{
    "event_id": "<event_id>",
    "room_id": "<room_id>"
}
```

The API returns a 400 if no announcement rooms are configured, and a 404 if
`id` is not in the configuration.

Push notifications are not yet sent for announcements.

---

## Statistics

*Source: [`docs/admin_api/statistics.md`](https://github.com/element-hq/synapse/blob/v1.165.0/docs/admin_api/statistics.md) · [rendered](https://element-hq.github.io/synapse/v1.165/admin_api/statistics.html)*

### Users' media usage statistics

Returns information about all local media usage of users. Gives the
possibility to filter them by time and user.

To use it, you will need to authenticate by providing an `access_token`
for a server admin: see [Admin API](#admin-api-introduction).

The API is:

```
GET /_synapse/admin/v1/statistics/users/media
```

A response body like the following is returned:

```json
{
  "users": [
    {
      "displayname": "foo_user_0",
      "media_count": 2,
      "media_length": 134,
      "user_id": "@foo_user_0:test"
    },
    {
      "displayname": "foo_user_1",
      "media_count": 2,
      "media_length": 134,
      "user_id": "@foo_user_1:test"
    }
  ],
  "next_token": 3,
  "total": 10
}
```

To paginate, check for `next_token` and if present, call the endpoint
again with `from` set to the value of `next_token`. This will return a new page.

If the endpoint does not return a `next_token` then there are no more
reports to paginate through.

**Parameters**

The following parameters should be set in the URL:

* `limit`: string representing a positive integer - Is optional but is
  used for pagination, denoting the maximum number of items to return
  in this call. Defaults to `100`.
* `from`: string representing a positive integer - Is optional but used for pagination,
  denoting the offset in the returned results. This should be treated as an opaque value
  and not explicitly set to anything other than the return value of `next_token` from a
  previous call. Defaults to `0`.
* `order_by` - string - The method in which to sort the returned list of users. Valid values are:
  - `user_id` - Users are ordered alphabetically by `user_id`. This is the default.
  - `displayname` - Users are ordered alphabetically by `displayname`.
  - `media_length` - Users are ordered by the total size of uploaded media in bytes.
    Smallest to largest.
  - `media_count` - Users are ordered by number of uploaded media. Smallest to largest.
* `from_ts` - string representing a positive integer - Considers only
  files created at this timestamp or later. Unix timestamp in ms.
* `until_ts` - string representing a positive integer - Considers only
  files created at this timestamp or earlier. Unix timestamp in ms.
* `search_term` - string - Filter users by their user ID localpart **or** displayname.
  The search term can be found in any part of the string.
  Defaults to no filtering.
* `dir` - string - Direction of order. Either `f` for forwards or `b` for backwards.
  Setting this value to `b` will reverse the above sort order. Defaults to `f`.


**Response**

The following fields are returned in the JSON response body:

* `users` - An array of objects, each containing information
  about the user and their local media. Objects contain the following fields:
  - `displayname` - string - Displayname of this user.
  - `media_count` - integer - Number of uploaded media by this user.
  - `media_length` - integer - Size of uploaded media in bytes by this user.
  - `user_id` - string - Fully-qualified user ID (ex. `@user:server.com`).
* `next_token` - integer - Opaque value used for pagination. See above.
* `total` - integer - Total number of users after filtering.


### Get largest rooms by size in database

Returns the 10 largest rooms and an estimate of how much space in the database
they are taking.

This does not include the size of any associated media associated with the room.

Returns an error on SQLite.

*Note:* This uses the planner statistics from PostgreSQL to do the estimates,
which means that the returned information can vary widely from reality. However,
it should be enough to get a rough idea of where database disk space is going.


The API is:

```
GET /_synapse/admin/v1/statistics/database/rooms
```

A response body like the following is returned:

```json
{
  "rooms": [
    {
      "room_id": "!OGEhHVWSdvArJzumhm:matrix.org",
      "estimated_size": 47325417353
    }
  ],
}
```



**Response**

The following fields are returned in the JSON response body:

* `rooms` - An array of objects, sorted by largest room first. Objects contain
  the following fields:
  - `room_id` - string - The room ID.
  - `estimated_size` - integer - Estimated disk space used in bytes by the room
    in the database.


*Added in Synapse 1.83.0*

---

## Users

*Source: [`docs/admin_api/user_admin_api.md`](https://github.com/element-hq/synapse/blob/v1.165.0/docs/admin_api/user_admin_api.md) · [rendered](https://element-hq.github.io/synapse/v1.165/admin_api/user_admin_api.html)*

### User Admin API

To use it, you will need to authenticate by providing an `access_token`
for a server admin: see [Admin API](#admin-api-introduction).

#### Query User Account

This API returns information about a specific user account.

The api is:

```
GET /_synapse/admin/v2/users/<user_id>
```

It returns a JSON body like the following:

```jsonc
{
    "name": "@user:example.com",
    "displayname": "User", // can be null if not set
    "threepids": [
        {
            "medium": "email",
            "address": "<user_mail_1>",
            "added_at": 1586458409743,
            "validated_at": 1586458409743
        },
        {
            "medium": "email",
            "address": "<user_mail_2>",
            "added_at": 1586458409743,
            "validated_at": 1586458409743
        }
    ],
    "avatar_url": "<avatar_url>",  // can be null if not set
    "is_guest": false,
    "admin": false,
    "deactivated": false,
    "erased": false,
    "shadow_banned": false,
    "creation_ts": 1560432506,
    "last_seen_ts": 1732919539393,
    "appservice_id": null,
    "consent_server_notice_sent": null,
    "consent_version": null,
    "consent_ts": null,
    "external_ids": [
        {
            "auth_provider": "<provider1>",
            "external_id": "<user_id_provider_1>"
        },
        {
            "auth_provider": "<provider2>",
            "external_id": "<user_id_provider_2>"
        }
    ],
    "user_type": null,
    "locked": false,
    "suspended": false
}
```

URL parameters:

- `user_id`: fully-qualified user id: for example, `@user:server.com`.

#### Create or modify account

This API allows an administrator to create or modify a user account with a
specific `user_id`.

This api is:

```
PUT /_synapse/admin/v2/users/<user_id>
```

with a body of:

```json
{
    "password": "user_password",
    "logout_devices": false,
    "displayname": "Alice Marigold",
    "avatar_url": "mxc://example.com/abcde12345",
    "threepids": [
        {
            "medium": "email",
            "address": "alice@example.com"
        },
        {
            "medium": "email",
            "address": "alice@domain.org"
        }
    ],
    "external_ids": [
        {
            "auth_provider": "example",
            "external_id": "12345"
        },
        {
            "auth_provider": "example2",
            "external_id": "abc54321"
        }
    ],
    "admin": false,
    "deactivated": false,
    "user_type": null,
    "locked": false
}
```

Returns HTTP status code:
- `201` - When a new user object was created.
- `200` - When a user was modified.

URL parameters:

- `user_id` - A fully-qualified user id. For example, `@user:server.com`.

Body parameters:

- `password` - **string**, optional. If provided, the user's password is updated and all
  devices are logged out, unless `logout_devices` is set to `false`.
- `logout_devices` - **bool**, optional, defaults to `true`. If set to `false`, devices aren't
  logged out even when `password` is provided.
- `displayname` - **string**, optional. If set to an empty string (`""`), the user's display name
  will be removed.
- `avatar_url` - **string**, optional. Must be a
  [MXC URI](https://matrix.org/docs/spec/client_server/r0.6.0#matrix-content-mxc-uris).
  If set to an empty string (`""`), the user's avatar is removed.
- `threepids` - **array**, optional. If provided, the user's third-party IDs (email, msisdn) are
  entirely replaced with the given list. Each item in the array is an object with the following
  fields:
  - `medium` - **string**, required. The type of third-party ID, either `email` or `msisdn` (phone number).
  - `address` - **string**, required. The third-party ID itself, e.g. `alice@example.com` for `email` or
    `447470274584` (for a phone number with country code "44") and `19254857364` (for a phone number
    with country code "1") for `msisdn`.
  Note: If a threepid is removed from a user via this option, Synapse will also attempt to remove
  that threepid from any identity servers it is aware has a binding for it.
- `external_ids` - **array**, optional. Allow setting the identifier of the external identity
  provider for SSO (Single sign-on). More details are in the configuration manual under the
  sections [sso](https://element-hq.github.io/synapse/v1.165/usage/configuration/config_documentation.html#sso) and [oidc_providers](https://element-hq.github.io/synapse/v1.165/usage/configuration/config_documentation.html#oidc_providers).
  - `auth_provider` - **string**, required. The unique, internal ID of the external identity provider.
    The same as `idp_id` from the homeserver configuration. If using OIDC, this value should be prefixed
    with `oidc-`. Note that no error is raised if the provided value is not in the homeserver configuration.
  - `external_id` - **string**, required. An identifier for the user in the external identity provider.
    When the user logs in to the identity provider, this must be the unique ID that they map to.
- `admin` - **bool**, optional, defaults to `false`. Whether the user is a homeserver administrator,
  granting them access to the Admin API, among other things.
- `deactivated` - **bool**, optional. If unspecified, deactivation state will be left unchanged.

  Note:
  - For the password field there is no strict check of the necessity for its presence.
    It is possible to have active users without a password, e.g. when authenticating with OIDC is configured.
    You must check yourself whether a password is required when reactivating a user or not.
  - It is not possible to set a password if the config option `password_config.localdb_enabled` is set `false`.
  Users' passwords are wiped upon account deactivation, hence the need to set a new one here.

  Note: a user cannot be erased with this API. For more details on
  deactivating and erasing users see [Deactivate Account](#deactivate-account).
- `locked` - **bool**, optional. If unspecified, locked state will be left unchanged.
- `user_type` - **string** or null, optional. If not provided, the user type will be
  not be changed. If `null` is given, the user type will be cleared.
  Other allowed options are: `bot` and `support` and any extra values defined in the homserver
  [configuration](https://element-hq.github.io/synapse/v1.165/usage/configuration/config_documentation.html#user_types).

#### List Accounts
##### List Accounts (V2)

This API returns all local user accounts.
By default, the response is ordered by ascending user ID.

```
GET /_synapse/admin/v2/users?from=0&limit=10&guests=false
```

A response body like the following is returned:

```json
{
    "users": [
        {
            "name": "<user_id1>",
            "is_guest": false,
            "admin": false,
            "user_type": null,
            "deactivated": false,
            "erased": false,
            "shadow_banned": false,
            "displayname": "<User One>",
            "avatar_url": null,
            "creation_ts": 1560432668000,
            "locked": false
        }, {
            "name": "<user_id2>",
            "is_guest": false,
            "admin": true,
            "user_type": null,
            "deactivated": false,
            "erased": false,
            "shadow_banned": false,
            "displayname": "<User Two>",
            "avatar_url": "<avatar_url>",
            "creation_ts": 1561550621000,
            "locked": false
        }
    ],
    "next_token": "100",
    "total": 200
}
```

To paginate, check for `next_token` and if present, call the endpoint again
with `from` set to the value of `next_token`. This will return a new page.

If the endpoint does not return a `next_token` then there are no more users
to paginate through.

**Parameters**

The following parameters should be set in the URL:

- `user_id` - Is optional and filters to only return users with user IDs
  that contain this value. This parameter is ignored when using the `name` parameter.
- `name` - Is optional and filters to only return users with user ID localparts
  **or** displaynames that contain this value.
- `guests` - string representing a bool - Is optional and if `false` will **exclude** guest users.
  Defaults to `true` to include guest users. Setting this option to `true` is not supported when Matrix Authentication
  Service integration is enabled. [See #15582](https://github.com/matrix-org/synapse/pull/15582)
- `admins` - Optional flag to filter admins. If `true`, only admins are queried. If `false`, admins are excluded from
  the query. When the flag is absent (the default), **both** admins and non-admins are included in the search results.
- `deactivated` - string representing a bool - Is optional and if `true` will **include** deactivated users.
  Defaults to `false` to exclude deactivated users.
- `limit` - string representing a positive integer - Is optional but is used for pagination,
  denoting the maximum number of items to return in this call. Defaults to `100`.
- `from` - string representing a positive integer - Is optional but used for pagination,
  denoting the offset in the returned results. This should be treated as an opaque value and
  not explicitly set to anything other than the return value of `next_token` from a previous call.
  Defaults to `0`.
- `order_by` - The method by which to sort the returned list of users.
  If the ordered field has duplicates, the second order is always by ascending `name`,
  which guarantees a stable ordering. Valid values are:

  - `name` - Users are ordered alphabetically by `name`. This is the default.
  - `is_guest` - Users are ordered by `is_guest` status.
  - `admin` - Users are ordered by `admin` status.
  - `user_type` - Users are ordered alphabetically by `user_type`.
  - `deactivated` - Users are ordered by `deactivated` status.
  - `shadow_banned` - Users are ordered by `shadow_banned` status.
  - `displayname` - Users are ordered alphabetically by `displayname`.
  - `avatar_url` - Users are ordered alphabetically by avatar URL.
  - `creation_ts` - Users are ordered by when the users was created in ms.
  - `last_seen_ts` - Users are ordered by when the user was lastly seen in ms.

- `dir` - Direction of media order. Either `f` for forwards or `b` for backwards.
  Setting this value to `b` will reverse the above sort order. Defaults to `f`.
- `not_user_type` - Exclude certain user types, such as bot users, from the request.
   Can be provided multiple times. Possible values are `bot`, `support` or "empty string".
   "empty string" here means to exclude users without a type.
- `locked` - string representing a bool - Is optional and if `true` will **include** locked users.
  Defaults to `false` to exclude locked users. Note: Introduced in v1.93.

Caution. The database only has indexes on the columns `name` and `creation_ts`.
This means that if a different sort order is used (`is_guest`, `admin`,
`user_type`, `deactivated`, `shadow_banned`, `avatar_url` or `displayname`),
this can cause a large load on the database, especially for large environments.

**Response**

The following fields are returned in the JSON response body:

- `users` - An array of objects, each containing information about an user.
  User objects contain the following fields:

  - `name` - string - Fully-qualified user ID (ex. `@user:server.com`).
  - `is_guest` - bool - Status if that user is a guest account.
  - `admin` - bool - Status if that user is a server administrator.
  - `user_type` - string - Type of the user. Normal users are type `None`.
    This allows user type specific behaviour. There are also types `support` and `bot`.
  - `deactivated` - bool - Status if that user has been marked as deactivated.
  - `erased` - bool - Status if that user has been marked as erased.
  - `shadow_banned` - bool - Status if that user has been marked as shadow banned.
  - `displayname` - string - The user's display name if they have set one.
  - `avatar_url` - string -  The user's avatar URL if they have set one.
  - `creation_ts` - integer - The user's creation timestamp in ms.
  - `last_seen_ts` - integer - The user's last activity timestamp in ms.
  - `locked` - bool - Status if that user has been marked as locked. Note: Introduced in v1.93.
- `next_token`: string representing a positive integer - Indication for pagination. See above.
- `total` - integer - Total number of media.

*Added in Synapse 1.93:* the `locked` query parameter and response field.

##### List Accounts (V3)

This API returns all local user accounts (see v2). In contrast to v2, the query parameter `deactivated` is handled differently.

```
GET /_synapse/admin/v3/users
```

**Parameters**
- `deactivated` - Optional flag to filter deactivated users. If `true`, only deactivated users are returned.
  If `false`, deactivated users are excluded from the query. When the flag is absent (the default),
  users are not filtered by deactivation status.

#### Query current sessions for a user

This API returns information about the active sessions for a specific user.

The endpoints are:

```
GET /_synapse/admin/v1/whois/<user_id>
```

and:

```
GET /_matrix/client/r0/admin/whois/<userId>
```

See also: [Client Server
API Whois](https://matrix.org/docs/spec/client_server/r0.6.1#get-matrix-client-r0-admin-whois-userid).

It returns a JSON body like the following:

```json
{
    "user_id": "<user_id>",
    "devices": {
        "": {
            "sessions": [
                {
                    "connections": [
                        {
                            "ip": "1.2.3.4",
                            "last_seen": 1417222374433,
                            "user_agent": "Mozilla/5.0 ..."
                        },
                        {
                            "ip": "1.2.3.10",
                            "last_seen": 1417222374500,
                            "user_agent": "Dalvik/2.1.0 ..."
                        }
                    ]
                }
            ]
        }
    }
}
```

`last_seen` is measured in milliseconds since the Unix epoch.

#### Deactivate Account

This API deactivates an account. It removes active access tokens, resets the
password, and deletes third-party IDs (to prevent the user requesting a
password reset).

It can also mark the user as GDPR-erased. This means messages sent by the
user will still be visible by anyone that was in the room when these messages
were sent, but hidden from users joining the room afterwards.

The api is:

```
POST /_synapse/admin/v1/deactivate/<user_id>
```

with a body of:

```json
{
    "erase": true
}
```

The erase parameter is optional and defaults to `false`.
An empty body may be passed for backwards compatibility.

The following actions are performed when deactivating an user:

- Try to unbind 3PIDs from the identity server
- Remove all 3PIDs from the homeserver
- Delete all devices and E2EE keys
- Delete all access tokens
- Delete all pushers
- Delete the password hash
- Removal from all rooms the user is a member of
- Remove the user from the user directory
- Reject all pending invites
- Remove all account validity information related to the user
- Remove the arbitrary data store known as *account data*. For example, this includes:
    - list of ignored users;
    - push rules;
    - secret storage keys; and
    - cross-signing keys.

The following additional actions are performed during deactivation if `erase`
is set to `true`:

- Remove the user's display name
- Remove the user's avatar URL
- Remove the user's custom profile fields
- Mark the user as erased

The following actions are **NOT** performed. The list may be incomplete.

- Remove mappings of SSO IDs
- [Delete media uploaded](#delete-media-uploaded-by-a-user-1) by user (included avatar images)
- Delete sent and received messages
- Remove the user's creation (registration) timestamp
- [Remove rate limit overrides](#override-ratelimiting-for-users)
- Remove from monthly active users
- Remove user's consent information (consent version and timestamp)

#### Suspend/Unsuspend Account

This API allows an admin to suspend/unsuspend an account. While an account is suspended, the user is 
prohibited from sending invites, joining or knocking on rooms, sending messages, changing profile data, and redacting messages other than their own. 

The api is:

```
PUT /_synapse/admin/v1/suspend/<user_id>
```

with a body of:

```json
{
    "suspend": true
}
```

To unsuspend a user, use the same endpoint with a body of:
```json
{
  "suspend": false
}
```

#### Reset password

**Note:** This API is disabled when Matrix Authentication Service integration is enabled. Use the [MAS Admin API](https://element-hq.github.io/matrix-authentication-service/topics/admin-api.html) or [the MAS CLI](https://element-hq.github.io/matrix-authentication-service/reference/cli/manage.html#manage-set-password) instead. An easy way to make use of this functionality is provided by [Element Admin](https://element.io/en/server-suite/admin) as part of [ESS](https://element.io/en/server-suite) Community and Pro.

Changes the password of another user. This will automatically log the user out of all their devices.

The api is:

```
POST /_synapse/admin/v1/reset_password/<user_id>
```

with a body of:

```json
{
   "new_password": "<secret>",
   "logout_devices": true
}
```

The parameter `new_password` is required.
The parameter `logout_devices` is optional and defaults to `true`.


#### Get whether a user is a server administrator or not

**Note:** This API is disabled when Matrix Authentication Service integration is enabled. Use the [MAS Admin API](https://element-hq.github.io/matrix-authentication-service/topics/admin-api.html) instead. An easy way to make use of this functionality is provided by [Element Admin](https://element.io/en/server-suite/admin) as part of [ESS](https://element.io/en/server-suite) Community and Pro.

The api is:

```
GET /_synapse/admin/v1/users/<user_id>/admin
```

A response body like the following is returned:

```json
{
    "admin": true
}
```


#### Change whether a user is a server administrator or not

**Note:** This API is disabled when Matrix Authentication Service integration is enabled. Use the [MAS Admin API](https://element-hq.github.io/matrix-authentication-service/topics/admin-api.html) or [the MAS CLI](https://element-hq.github.io/matrix-authentication-service/reference/cli/manage.html#manage-promote-admin) instead. An easy way to make use of this functionality is provided by [Element Admin](https://element.io/en/server-suite/admin) as part of [ESS](https://element.io/en/server-suite) Community and Pro.

Note that you cannot demote yourself.

The api is:

```
PUT /_synapse/admin/v1/users/<user_id>/admin
```

with a body of:

```json
{
    "admin": true
}
```

#### List room memberships of a user

Gets a list of room memberships for a specific `user_id`. This
endpoint differs from
[`GET /_synapse/admin/v1/users/<user_id>/joined_rooms`](#list-joined-rooms-of-a-user)
in that it returns rooms with memberships other than "join".

The API is:

```
GET /_synapse/admin/v1/users/<user_id>/memberships
```

A response body like the following is returned:

```json
    {
        "memberships": {
            "!DuGcnbhHGaSZQoNQR:matrix.org": "join",
            "!ZtSaPCawyWtxfWiIy:matrix.org": "leave",
        }
    }
```

which is a list of room membership states for the given user. This endpoint can
be used with both local and remote users, with the caveat that the homeserver will
only be aware of the memberships for rooms that one of its local users has joined.

Remote user memberships may also be out of date if all local users have since left
a room. The homeserver will thus no longer receive membership updates about it.

The list includes rooms that the user has since left; other membership states (knock,
invite, etc.) are also possible.

Note that rooms will only disappear from this list if they are
[purged](#delete-room-api) from the homeserver.

**Parameters**

The following parameters should be set in the URL:

- `user_id` - fully qualified: for example, `@user:server.com`.

**Response**

The following fields are returned in the JSON response body:

- `memberships` - A map of `room_id` (string) to `membership` state (string).

#### List joined rooms of a user

Gets a list of all `room_id` that a specific `user_id` is joined to and is a member of (participating in).

The API is:

```
GET /_synapse/admin/v1/users/<user_id>/joined_rooms
```

A response body like the following is returned:

```json
    {
        "joined_rooms": [
            "!DuGcnbhHGaSZQoNQR:matrix.org",
            "!ZtSaPCawyWtxfWiIy:matrix.org"
        ],
        "total": 2
    }
```

The server returns the list of rooms of which the user and the server
are member. If the user is local, all the rooms of which the user is
member are returned.

**Parameters**

The following parameters should be set in the URL:

- `user_id` - fully qualified: for example, `@user:server.com`.

**Response**

The following fields are returned in the JSON response body:

- `joined_rooms` - An array of `room_id`.
- `total` - Number of rooms.

#### Get the number of invites sent by the user

Fetches the number of invites sent by the provided user ID across all rooms
after the given timestamp.

```
GET /_synapse/admin/v1/users/<user_id>/sent_invite_count
```

**Parameters**

The following parameters should be set in the URL:

* `user_id`: fully qualified: for example, `@user:server.com`

The following should be set as query parameters in the URL:

* `from_ts`: int, required. A timestamp in ms from the unix epoch. Only
   invites sent at or after the provided timestamp will be returned.
   This works by comparing the provided timestamp to the `received_ts`
   column in the `events` table.
   Note: https://currentmillis.com/ is a useful tool for converting dates
   into timestamps and vice versa.

A response body like the following is returned:

```json
{
  "invite_count": 30
}
```

_Added in Synapse 1.122.0_

#### Get the cumulative number of rooms a user has joined after a given timestamp

Fetches the number of rooms that the user joined after the given timestamp, even
if they have subsequently left/been banned from those rooms.

```
GET /_synapse/admin/v1/users/<user_id>/cumulative_joined_room_count
```

**Parameters**

The following parameters should be set in the URL:

* `user_id`: fully qualified: for example, `@user:server.com`

The following should be set as query parameters in the URL:

* `from_ts`: int, required. A timestamp in ms from the unix epoch. Only
   invites sent at or after the provided timestamp will be returned.
   This works by comparing the provided timestamp to the `received_ts`
   column in the `events` table.
   Note: https://currentmillis.com/ is a useful tool for converting dates
   into timestamps and vice versa.

A response body like the following is returned:

```json
{
  "cumulative_joined_room_count": 30
}
```
_Added in Synapse 1.122.0_

#### Account Data
Gets information about account data for a specific `user_id`.

The API is:

```
GET /_synapse/admin/v1/users/<user_id>/accountdata
```

A response body like the following is returned:

```json
{
    "account_data": {
        "global": {
            "m.secret_storage.key.LmIGHTg5W": {
                "algorithm": "m.secret_storage.v1.aes-hmac-sha2",
                "iv": "fwjNZatxg==",
                "mac": "eWh9kNnLWZUNOgnc="
            },
            "im.vector.hide_profile": {
                "hide_profile": true
            },
            "org.matrix.preview_urls": {
                "disable": false
            },
            "im.vector.riot.breadcrumb_rooms": {
                "rooms": [
                    "!LxcBDAsDUVAfJDEo:matrix.org",
                    "!MAhRxqasbItjOqxu:matrix.org"
                ]
            },
            "m.accepted_terms": {
                "accepted": [
                    "https://example.org/somewhere/privacy-1.2-en.html",
                    "https://example.org/somewhere/terms-2.0-en.html"
                ]
            },
            "im.vector.setting.breadcrumbs": {
                "recent_rooms": [
                    "!MAhRxqasbItqxuEt:matrix.org",
                    "!ZtSaPCawyWtxiImy:matrix.org"
                ]
            }
        },
        "rooms": {
            "!GUdfZSHUJibpiVqHYd:matrix.org": {
                "m.fully_read": {
                    "event_id": "$156334540fYIhZ:matrix.org"
                }
            },
            "!tOZwOOiqwCYQkLhV:matrix.org": {
                "m.fully_read": {
                    "event_id": "$xjsIyp4_NaVl2yPvIZs_k1Jl8tsC_Sp23wjqXPno"
                }
            }
        }
    }
}
```

**Parameters**

The following parameters should be set in the URL:

- `user_id` - fully qualified: for example, `@user:server.com`.

**Response**

The following fields are returned in the JSON response body:

- `account_data` - A map containing the account data for the user
  - `global` - A map containing the global account data for the user
  - `rooms` - A map containing the account data per room for the user

#### User media

##### List media uploaded by a user
Gets a list of all local media that a specific `user_id` has created.
These are media that the user has uploaded themselves
([local media](https://element-hq.github.io/synapse/v1.165/media_repository.html#local-media)), as well as
[URL preview images](https://element-hq.github.io/synapse/v1.165/media_repository.html#url-previews) requested by the user if the
[feature is enabled](https://element-hq.github.io/synapse/v1.165/usage/configuration/config_documentation.html#url_preview_enabled).

By default, the response is ordered by descending creation date and ascending media ID.
The newest media is on top. You can change the order with parameters
`order_by` and `dir`.

The API is:

```
GET /_synapse/admin/v1/users/<user_id>/media
```

A response body like the following is returned:

```json
{
  "media": [
    {
      "created_ts": 100400,
      "last_access_ts": null,
      "media_id": "qXhyRzulkwLsNHTbpHreuEgo",
      "media_length": 67,
      "media_type": "image/png",
      "quarantined_by": null,
      "safe_from_quarantine": false,
      "upload_name": "test1.png"
    },
    {
      "created_ts": 200400,
      "last_access_ts": null,
      "media_id": "FHfiSnzoINDatrXHQIXBtahw",
      "media_length": 67,
      "media_type": "image/png",
      "quarantined_by": null,
      "safe_from_quarantine": false,
      "upload_name": "test2.png"
    },
    {
      "created_ts": 300400,
      "last_access_ts": 300700,
      "media_id": "BzYNLRUgGHphBkdKGbzXwbjX",
      "media_length": 1337,
      "media_type": "application/octet-stream",
      "quarantined_by": null,
      "safe_from_quarantine": false,
      "upload_name": null
    }
  ],
  "next_token": 3,
  "total": 2
}
```

To paginate, check for `next_token` and if present, call the endpoint again
with `from` set to the value of `next_token`. This will return a new page.

If the endpoint does not return a `next_token` then there are no more
reports to paginate through.

**Parameters**

The following parameters should be set in the URL:

- `user_id` - string - fully qualified: for example, `@user:server.com`.
- `limit`: string representing a positive integer - Is optional but is used for pagination,
  denoting the maximum number of items to return in this call. Defaults to `100`.
- `from`: string representing a positive integer - Is optional but used for pagination,
  denoting the offset in the returned results. This should be treated as an opaque value and
  not explicitly set to anything other than the return value of `next_token` from a previous call.
  Defaults to `0`.
- `order_by` - The method by which to sort the returned list of media.
  If the ordered field has duplicates, the second order is always by ascending `media_id`,
  which guarantees a stable ordering. Valid values are:

  - `media_id` - Media are ordered alphabetically by `media_id`.
  - `upload_name` - Media are ordered alphabetically by name the media was uploaded with.
  - `created_ts` - Media are ordered by when the content was uploaded in ms.
    Smallest to largest. This is the default.
  - `last_access_ts` - Media are ordered by when the content was last accessed in ms.
    Smallest to largest.
  - `media_length` - Media are ordered by length of the media in bytes.
    Smallest to largest.
  - `media_type` - Media are ordered alphabetically by MIME-type.
  - `quarantined_by` - Media are ordered alphabetically by the user ID that
    initiated the quarantine request for this media.
  - `safe_from_quarantine` - Media are ordered by the status if this media is safe
    from quarantining.

- `dir` - Direction of media order. Either `f` for forwards or `b` for backwards.
  Setting this value to `b` will reverse the above sort order. Defaults to `f`.

If neither `order_by` nor `dir` is set, the default order is newest media on top
(corresponds to `order_by` = `created_ts` and `dir` = `b`).

Caution. The database only has indexes on the columns `media_id`,
`user_id` and `created_ts`. This means that if a different sort order is used
(`upload_name`, `last_access_ts`, `media_length`, `media_type`,
`quarantined_by` or `safe_from_quarantine`), this can cause a large load on the
database, especially for large environments.

**Response**

The following fields are returned in the JSON response body:

- `media` - An array of objects, each containing information about a media.
  Media objects contain the following fields:
  - `created_ts` - integer - Timestamp when the content was uploaded in ms.
  - `last_access_ts` - integer or null - Timestamp when the content was last accessed in ms.
     Null if there was no access, yet.
  - `media_id` - string - The id used to refer to the media. Details about the format
    are documented under
    [media repository](https://element-hq.github.io/synapse/v1.165/media_repository.html).
  - `media_length` - integer - Length of the media in bytes.
  - `media_type` - string - The MIME-type of the media.
  - `quarantined_by` - string or null - The user ID that initiated the quarantine request
    for this media. Null if not quarantined.
  - `safe_from_quarantine` - bool - Status if this media is safe from quarantining.
  - `upload_name` - string or null - The name the media was uploaded with. Null if not provided during upload.
- `next_token`: integer - Indication for pagination. See above.
- `total` - integer - Total number of media.

##### Delete media uploaded by a user

This API deletes the *local* media from the disk of your own server
that a specific `user_id` has created. This includes any local thumbnails.

This API will not affect media that has been uploaded to external
media repositories (e.g https://github.com/turt2live/matrix-media-repo/).

By default, the API deletes media ordered by descending creation date and ascending media ID.
The newest media is deleted first. You can change the order with parameters
`order_by` and `dir`. If no `limit` is set the API deletes `100` files per request.

The API is:

```
DELETE /_synapse/admin/v1/users/<user_id>/media
```

A response body like the following is returned:

```json
{
  "deleted_media": [
    "abcdefghijklmnopqrstuvwx"
  ],
  "total": 1
}
```

The following fields are returned in the JSON response body:

* `deleted_media`: an array of strings - List of deleted `media_id`
* `total`: integer - Total number of deleted `media_id`

**Note**: There is no `next_token`. This is not useful for deleting media, because
after deleting media the remaining media have a new order.

**Parameters**

This API has the same parameters as
[List media uploaded by a user](#list-media-uploaded-by-a-user).
With the parameters you can for example limit the number of files to delete at once or
delete largest/smallest or newest/oldest files first.

#### Login as a user

**Note:** This API is disabled when Matrix Authentication Service integration is enabled. Use [Personal sessions](https://element-hq.github.io/matrix-authentication-service/topics/authorization.html#personal-sessions-personal-access-tokens) through the [MAS Admin API](https://element-hq.github.io/matrix-authentication-service/topics/admin-api.html) instead. An easy way to make use of this functionality is provided by [Element Admin](https://element.io/en/server-suite/admin) as part of [ESS](https://element.io/en/server-suite) Community and Pro.

Get an access token that can be used to authenticate as that user. Useful for
when admins wish to do actions on behalf of a user.

The API is:

```
POST /_synapse/admin/v1/users/<user_id>/login
{}
```

An optional `valid_until_ms` field can be specified in the request body as an
integer timestamp that specifies when the token should expire. By default tokens
do not expire. Note that this API does not allow a user to login as themselves
(to create more tokens).

A response body like the following is returned:

```json
{
    "access_token": "<opaque_access_token_string>"
}
```

This API does *not* generate a new device for the user, and so will not appear
their `/devices` list, and in general the target user should not be able to
tell they have been logged in as.

To expire the token call the standard `/logout` API with the token.

Note: The token will expire if the *admin* user calls `/logout/all` from any
of their devices, but the token will *not* expire if the target user does the
same.

#### Allow replacing master cross-signing key without User-Interactive Auth

This endpoint is not intended for server administrator usage;
we describe it here for completeness.

This API temporarily permits a user to replace their master cross-signing key
without going through
[user-interactive authentication](https://spec.matrix.org/v1.8/client-server-api/#user-interactive-authentication-api) (UIA).
This is useful when Synapse has delegated its authentication to the
[Matrix Authentication Service](https://github.com/matrix-org/matrix-authentication-service/);
as Synapse cannot perform UIA is not possible in these circumstances.

The API is

```http request
POST /_synapse/admin/v1/users/<user_id>/_allow_cross_signing_replacement_without_uia
{}
```

If the user does not exist, or does exist but has no master cross-signing key,
this will return with status code `404 Not Found`.

Otherwise, a response body like the following is returned, with status `200 OK`:

```json
{
    "updatable_without_uia_before_ms": 1234567890
}
```

The response body is a JSON object with a single field:

- `updatable_without_uia_before_ms`: integer. The timestamp in milliseconds
  before which the user is permitted to replace their cross-signing key without
  going through UIA.

_Added in Synapse 1.97.0._

#### User devices

##### List all devices
Gets information about all devices for a specific `user_id`.

The API is:

```
GET /_synapse/admin/v2/users/<user_id>/devices
```

A response body like the following is returned:

```json
{
  "devices": [
    {
      "device_id": "QBUAZIFURK",
      "display_name": "android",
      "last_seen_ip": "1.2.3.4",
      "last_seen_user_agent": "Mozilla/5.0 (X11; Linux x86_64; rv:103.0) Gecko/20100101 Firefox/103.0",
      "last_seen_ts": 1474491775024,
      "user_id": "<user_id>",
      "dehydrated": false
    },
    {
      "device_id": "AUIECTSRND",
      "display_name": "ios",
      "last_seen_ip": "1.2.3.5",
      "last_seen_user_agent": "Mozilla/5.0 (X11; Linux x86_64; rv:103.0) Gecko/20100101 Firefox/103.0",
      "last_seen_ts": 1474491775025,
      "user_id": "<user_id>",
      "dehydrated": false
    }
  ],
  "total": 2
}
```

**Parameters**

The following parameters should be set in the URL:

- `user_id` - fully qualified: for example, `@user:server.com`.

**Response**

The following fields are returned in the JSON response body:

- `devices` - An array of objects, each containing information about a device.
  Device objects contain the following fields:

  - `device_id` - Identifier of device.
  - `display_name` - Display name set by the user for this device.
    Absent if no name has been set.
  - `last_seen_ip` - The IP address where this device was last seen.
    (May be a few minutes out of date, for efficiency reasons).
  - `last_seen_user_agent` - The user agent of the device when it was last seen.
    (May be a few minutes out of date, for efficiency reasons).
  - `last_seen_ts` - The timestamp (in milliseconds since the unix epoch) when this
    devices was last seen. (May be a few minutes out of date, for efficiency reasons).
  - `user_id` - Owner of  device.
  - `dehydrated` - Whether the device is a dehydrated device.

- `total` - Total number of user's devices.

##### Create a device

Creates a new device for a specific `user_id` and `device_id`. Does nothing if the `device_id`
exists already.

The API is:

```
POST /_synapse/admin/v2/users/<user_id>/devices

{
  "device_id": "QBUAZIFURK"
}
```

An empty JSON dict is returned.

**Parameters**

The following parameters should be set in the URL:

- `user_id` - fully qualified: for example, `@user:server.com`.

The following fields are required in the JSON request body:

- `device_id` - The device ID to create.

##### Delete multiple devices
Deletes the given devices for a specific `user_id`, and invalidates
any access token associated with them.

The API is:

```
POST /_synapse/admin/v2/users/<user_id>/delete_devices

{
  "devices": [
    "QBUAZIFURK",
    "AUIECTSRND"
  ]
}
```

An empty JSON dict is returned.

**Parameters**

The following parameters should be set in the URL:

- `user_id` - fully qualified: for example, `@user:server.com`.

The following fields are required in the JSON request body:

- `devices` - The list of device IDs to delete.

##### Show a device
Gets information on a single device, by `device_id` for a specific `user_id`.

The API is:

```
GET /_synapse/admin/v2/users/<user_id>/devices/<device_id>
```

A response body like the following is returned:

```json
{
  "device_id": "<device_id>",
  "display_name": "android",
  "last_seen_ip": "1.2.3.4",
  "last_seen_user_agent": "Mozilla/5.0 (X11; Linux x86_64; rv:103.0) Gecko/20100101 Firefox/103.0",
  "last_seen_ts": 1474491775024,
  "user_id": "<user_id>"
}
```

**Parameters**

The following parameters should be set in the URL:

- `user_id` - fully qualified: for example, `@user:server.com`.
- `device_id` - The device to retrieve.

**Response**

The following fields are returned in the JSON response body:

- `device_id` - Identifier of device.
- `display_name` - Display name set by the user for this device.
  Absent if no name has been set.
- `last_seen_ip` - The IP address where this device was last seen.
  (May be a few minutes out of date, for efficiency reasons).
  - `last_seen_user_agent` - The user agent of the device when it was last seen.
  (May be a few minutes out of date, for efficiency reasons).
- `last_seen_ts` - The timestamp (in milliseconds since the unix epoch) when this
  devices was last seen. (May be a few minutes out of date, for efficiency reasons).
- `user_id` - Owner of  device.

##### Update a device
Updates the metadata on the given `device_id` for a specific `user_id`.

The API is:

```
PUT /_synapse/admin/v2/users/<user_id>/devices/<device_id>

{
  "display_name": "My other phone"
}
```

An empty JSON dict is returned.

**Parameters**

The following parameters should be set in the URL:

- `user_id` - fully qualified: for example, `@user:server.com`.
- `device_id` - The device to update.

The following fields are required in the JSON request body:

- `display_name` - The new display name for this device. If not given,
  the display name is unchanged.

##### Delete a device
Deletes the given `device_id` for a specific `user_id`,
and invalidates any access token associated with it.

The API is:

```
DELETE /_synapse/admin/v2/users/<user_id>/devices/<device_id>

{}
```

An empty JSON dict is returned.

**Parameters**

The following parameters should be set in the URL:

- `user_id` - fully qualified: for example, `@user:server.com`.
- `device_id` - The device to delete.

#### List all pushers
Gets information about all pushers for a specific `user_id`.

The API is:

```
GET /_synapse/admin/v1/users/<user_id>/pushers
```

A response body like the following is returned:

```json
{
  "pushers": [
    {
      "app_display_name":"HTTP Push Notifications",
      "app_id":"m.http",
      "data": {
        "url":"example.com"
      },
      "device_display_name":"pushy push",
      "kind":"http",
      "lang":"None",
      "profile_tag":"",
      "pushkey":"a@example.com"
    }
  ],
  "total": 1
}
```

**Parameters**

The following parameters should be set in the URL:

- `user_id` - fully qualified: for example, `@user:server.com`.

**Response**

The following fields are returned in the JSON response body:

- `pushers` - An array containing the current pushers for the user

  - `app_display_name` - string - A string that will allow the user to identify
    what application owns this pusher.

  - `app_id` - string - This is a reverse-DNS style identifier for the application.
    Max length, 64 chars.

  - `data` - A dictionary of information for the pusher implementation itself.

    - `url` - string - Required if `kind` is `http`. The URL to use to send
      notifications to.

    - `format` - string - The format to use when sending notifications to the
      Push Gateway.

  - `device_display_name` - string -  A string that will allow the user to identify
    what device owns this pusher.

  - `profile_tag` - string - This string determines which set of device specific rules
    this pusher executes.

  - `kind` - string -  The kind of pusher. "http" is a pusher that sends HTTP pokes.
  - `lang` - string - The preferred language for receiving notifications
    (e.g. 'en' or 'en-US')

  - `profile_tag` - string - This string determines which set of device specific rules
    this pusher executes.

  - `pushkey` - string - This is a unique identifier for this pusher.
    Max length, 512 bytes.

- `total` - integer - Number of pushers.

See also the
[Client-Server API Spec on pushers](https://matrix.org/docs/spec/client_server/latest#get-matrix-client-r0-pushers).

#### Controlling whether a user is shadow-banned

Shadow-banning is a useful tool for moderating malicious or egregiously abusive users. 
A shadow-banned users receives successful responses to their client-server API requests,
but the events are not propagated into rooms. This can be an effective tool as it
(hopefully) takes longer for the user to realise they are being moderated before
pivoting to another account.

Shadow-banning a user should be used as a tool of last resort and may lead to confusing
or broken behaviour for the client. A shadow-banned user will not receive any
notification and it is generally more appropriate to ban or kick abusive users.
A shadow-banned user will be unable to contact anyone on the server.

To shadow-ban a user the API is:

```
POST /_synapse/admin/v1/users/<user_id>/shadow_ban
```

To un-shadow-ban a user the API is:

```
DELETE /_synapse/admin/v1/users/<user_id>/shadow_ban
```

An empty JSON dict is returned in both cases.

**Parameters**

The following parameters should be set in the URL:

- `user_id` - The fully qualified MXID: for example, `@user:server.com`. The user must
  be local.

#### Override ratelimiting for users

This API allows to override or disable ratelimiting for a specific user.
There are specific APIs to set, get and delete a ratelimit.

##### Get status of ratelimit

The API is:

```
GET /_synapse/admin/v1/users/<user_id>/override_ratelimit
```

A response body like the following is returned:

```json
{
  "messages_per_second": 0,
  "burst_count": 0
}
```

**Parameters**

The following parameters should be set in the URL:

- `user_id` - The fully qualified MXID: for example, `@user:server.com`. The user must
  be local.

**Response**

The following fields are returned in the JSON response body:

- `messages_per_second` - integer - The number of actions that can
  be performed in a second. `0` mean that ratelimiting is disabled for this user.
- `burst_count` - integer - How many actions that can be performed before
  being limited.

If **no** custom ratelimit is set, an empty JSON dict is returned.

```json
{}
```

##### Set ratelimit

The API is:

```
POST /_synapse/admin/v1/users/<user_id>/override_ratelimit
```

A response body like the following is returned:

```json
{
  "messages_per_second": 0,
  "burst_count": 0
}
```

**Parameters**

The following parameters should be set in the URL:

- `user_id` - The fully qualified MXID: for example, `@user:server.com`. The user must
  be local.

Body parameters:

- `messages_per_second` - positive integer, optional. The number of actions that can
  be performed in a second. Defaults to `0`.
- `burst_count` - positive integer, optional. How many actions that can be performed
  before being limited. Defaults to `0`.

To disable users' ratelimit set both values to `0`.

**Response**

The following fields are returned in the JSON response body:

- `messages_per_second` - integer - The number of actions that can
  be performed in a second.
- `burst_count` - integer - How many actions that can be performed before
  being limited.

##### Delete ratelimit

The API is:

```
DELETE /_synapse/admin/v1/users/<user_id>/override_ratelimit
```

An empty JSON dict is returned.

```json
{}
```

**Parameters**

The following parameters should be set in the URL:

- `user_id` - The fully qualified MXID: for example, `@user:server.com`. The user must
  be local.

#### Check username availability

Checks to see if a username is available, and valid, for the server. See [the client-server
API](https://matrix.org/docs/spec/client_server/r0.6.0#get-matrix-client-r0-register-available)
for more information.

This endpoint will work even if registration is disabled on the server, unlike
`/_matrix/client/r0/register/available`.

The API is:

```
GET /_synapse/admin/v1/username_available?username=$localpart
```

The request and response format is the same as the
[/_matrix/client/r0/register/available](https://matrix.org/docs/spec/client_server/r0.6.0#get-matrix-client-r0-register-available) API.

#### Find a user based on their ID in an auth provider

The API is:

```
GET /_synapse/admin/v1/auth_providers/<provider>/users/<external_id>
```

When a user matched the given ID for the given provider, an HTTP code `200` with a response body like the following is returned:

```json
{
    "user_id": "@hello:example.org"
}
```

**Parameters**

The following parameters should be set in the URL:

- `provider` - The ID of the authentication provider, as advertised by the [`GET /_matrix/client/v3/login`](https://spec.matrix.org/latest/client-server-api/#post_matrixclientv3login) API in the `m.login.sso` authentication method.
- `external_id` - The user ID from the authentication provider. Usually corresponds to the `sub` claim for OIDC providers, or to the `uid` attestation for SAML2 providers.

The `external_id` may have characters that are not URL-safe (typically `/`, `:` or `@`), so it is advised to URL-encode those parameters.

**Errors**

Returns a `404` HTTP status code if no user was found, with a response body like this:

```json
{
    "errcode":"M_NOT_FOUND",
    "error":"User not found"
}
```

_Added in Synapse 1.68.0._


#### Find a user based on their Third Party ID (ThreePID or 3PID)

The API is:

```
GET /_synapse/admin/v1/threepid/<medium>/users/<address>
```

When a user matched the given address for the given medium, an HTTP code `200` with a response body like the following is returned:

```json
{
    "user_id": "@hello:example.org"
}
```

**Parameters**

The following parameters should be set in the URL:

- `medium` - Kind of third-party ID, either `email` or `msisdn`.
- `address` - Value of the third-party ID.

The `address` may have characters that are not URL-safe, so it is advised to URL-encode those parameters.

**Errors**

Returns a `404` HTTP status code if no user was found, with a response body like this:

```json
{
    "errcode":"M_NOT_FOUND",
    "error":"User not found"
}
```

_Added in Synapse 1.72.0._


#### Redact events of a user

This endpoint allows an admin to redact the events of a given user. There are no restrictions on
redactions for a local user. By default, we puppet the user who sent the message to redact it themselves.
Redactions for non-local users are issued using the admin user, and will fail in rooms where the
admin user is not admin/does not have the specified power level to issue redactions. An option
is provided to override the default and allow the admin to issue the redactions in all cases.
There are optional parameters to filter for events that happened in the given time period. 

The API is 
```
POST /_synapse/admin/v1/user/<user_id>/redact

{
  "rooms": ["!roomid1", "!roomid2"],
  "after_ts": 1779564103728,
  "before_ts": 1779564103730
}
```
If an empty list is provided as the key for `rooms`, all events in all the rooms the user is member of will be redacted,
otherwise all the events in the rooms provided in the request will be redacted. 
If neither `after_ts` nor `before_ts` is provided, events will be redacted regardless of when they happened. If only one parameter is provided, all events occurring on or before/after given time will be redacted.

The API starts redaction process running, and returns immediately with a JSON body with
a redact id which can be used to query the status of the redaction process:

```json
{
    "redact_id": "<opaque id>"
}
```

**Parameters**

The following parameters should be set in the URL:

- `user_id` - The fully qualified MXID of the user: for example, `@user:server.com`.

The following JSON body parameter must be provided:

-  `rooms` - A list of rooms to redact the user's events in. If an empty list is provided all events in all rooms
  the user is a member of will be redacted

The following JSON body parameters are optional:

- `reason` - Reason the redaction is being requested, ie "spam", "abuse", etc. This will be included in each redaction event, and be visible to users.
- `limit` - a limit on the number of the user's events to search for ones that can be redacted (events are redacted newest to oldest) in each room, defaults to 1000 if not provided.
- `use_admin` - If set to `true`, the admin user is used to issue the redactions, rather than puppeting the user. Useful
 when the admin is also the moderator of the rooms that require redactions. Note that the redactions will fail in rooms
 where the admin does not have the sufficient power level to issue the redactions.
- `after_ts` - Redact only events that were sent at this time or after. Format: milliseconds timestamp. _Added in Synapse 1.157.0._
- `before_ts` - Redact only events that were sent at this time or before. Format: milliseconds timestamp. _Added in Synapse 1.157.0._

_Added in Synapse 1.116.0._


#### Check the status of a redaction process

It is possible to query the status of the background task for redacting a user's events.
The status can be queried up to 24 hours after completion of the task,
or until Synapse is restarted (whichever happens first).

The API is:

```
GET /_synapse/admin/v1/user/redact_status/<redact_id>
```

A response body like the following is returned:

```
{
  "status": "complete",
  "failed_redactions": {},
}
```

**Parameters**

The following parameters should be set in the URL:

* `redact_id` - string - The ID for this redaction process, provided when the redaction was requested.


**Response**

The following fields are returned in the JSON response body:

- `status` - string (required) - one of `scheduled`/`active`/`complete`/`failed`,
  indicating the status of the redaction job
- `failed_redactions` - dictionary (optional) - when the redaction status is `complete`,
  contains the keys of the dict are event ids the process was unable to redact, if any,
  and the values are the corresponding error that caused the redaction to fail
- `error` - string (optional) - when the redaction `status` is `failed`, this field will
  contains a description of the error encountered during the redaction process.

_Added in Synapse 1.116.0._

---

## Server Version

*Source: [`docs/admin_api/version_api.md`](https://github.com/element-hq/synapse/blob/v1.165.0/docs/admin_api/version_api.md) · [rendered](https://element-hq.github.io/synapse/v1.165/admin_api/version_api.html)*

### Version API

This API returns the running Synapse version.
This is useful when a Synapse instance
is behind a proxy that does not forward the 'Server' header (which also
contains Synapse version information).

The api is:

```
GET /_synapse/admin/v1/server_version
```

It returns a JSON body like the following:

```json
{
    "server_version": "0.99.2rc1 (b=develop, abcdef123)"
}
```

*Changed in Synapse 1.94.0:* The `python_version` key was removed from the
response body.

---

## Federation

*Source: [`docs/usage/administration/admin_api/federation.md`](https://github.com/element-hq/synapse/blob/v1.165.0/docs/usage/administration/admin_api/federation.md) · [rendered](https://element-hq.github.io/synapse/v1.165/usage/administration/admin_api/federation.html)*

### Federation API

This API allows a server administrator to manage Synapse's federation with other homeservers.

Note: This API is new, experimental and "subject to change".

#### List of destinations

This API gets the current destination retry timing info for all remote servers.

The list contains all the servers with which the server federates,
regardless of whether an error occurred or not.
If an error occurs, it may take up to 20 minutes for the error to be displayed here,
as a complete retry must have failed.

The API is:

A standard request with no filtering:

```
GET /_synapse/admin/v1/federation/destinations
```

A response body like the following is returned:

```json
{
   "destinations":[
      {
         "destination": "matrix.org",
         "retry_last_ts": 1557332397936,
         "retry_interval": 3000000,
         "failure_ts": 1557329397936,
         "last_successful_stream_ordering": null
      }
   ],
   "total": 1
}
```

To paginate, check for `next_token` and if present, call the endpoint again
with `from` set to the value of `next_token`. This will return a new page.

If the endpoint does not return a `next_token` then there are no more destinations
to paginate through.

**Parameters**

The following query parameters are available:

- `from` - Offset in the returned list. Defaults to `0`.
- `limit` - Maximum amount of destinations to return. Defaults to `100`.
- `order_by` - The method in which to sort the returned list of destinations.
  Valid values are:
  - `destination` - Destinations are ordered alphabetically by remote server name.
    This is the default.
  - `retry_last_ts` - Destinations are ordered by time of last retry attempt in ms.
  - `retry_interval` - Destinations are ordered by how long until next retry in ms.
  - `failure_ts` - Destinations are ordered by when the server started failing in ms.
  - `last_successful_stream_ordering` - Destinations are ordered by the stream ordering
    of the most recent successfully-sent PDU.
- `dir` - Direction of room order. Either `f` for forwards or `b` for backwards. Setting
  this value to `b` will reverse the above sort order. Defaults to `f`.

*Caution:* The database only has an index on the column `destination`.
This means that if a different sort order is used,
this can cause a large load on the database, especially for large environments.

**Response**

The following fields are returned in the JSON response body:

- `destinations` - An array of objects, each containing information about a destination.
  Destination objects contain the following fields:
  - `destination` - string - Name of the remote server to federate.
  - `retry_last_ts` - integer - The last time Synapse tried and failed to reach the
    remote server, in ms. This is `0` if the last attempt to communicate with the
    remote server was successful.
  - `retry_interval` - integer - How long since the last time Synapse tried to reach
    the remote server before trying again, in ms. This is `0` if no further retrying occurring.
  - `failure_ts` - nullable integer - The first time Synapse tried and failed to reach the
    remote server, in ms. This is `null` if communication with the remote server has never failed.
  - `last_successful_stream_ordering` - nullable integer - The stream ordering of the most
    recent successfully-sent [PDU](https://element-hq.github.io/synapse/v1.165/usage/administration/understanding_synapse_through_grafana_graphs.html#federation)
    to this destination, or `null` if this information has not been tracked yet.
- `next_token`: string representing a positive integer - Indication for pagination. See above.
- `total` - integer - Total number of destinations.

#### Destination Details API

This API gets the retry timing info for a specific remote server.

The API is:

```
GET /_synapse/admin/v1/federation/destinations/<destination>
```

A response body like the following is returned:

```json
{
   "destination": "matrix.org",
   "retry_last_ts": 1557332397936,
   "retry_interval": 3000000,
   "failure_ts": 1557329397936,
   "last_successful_stream_ordering": null
}
```

**Parameters**

The following parameters should be set in the URL:

- `destination` - Name of the remote server.

**Response**

The response fields are the same like in the `destinations` array in
[List of destinations](#list-of-destinations) response.

#### Destination rooms

This API gets the rooms that federate with a specific remote server.

The API is:

```
GET /_synapse/admin/v1/federation/destinations/<destination>/rooms
```

A response body like the following is returned:

```json
{
   "rooms":[
      {
         "room_id": "!OGEhHVWSdvArJzumhm:matrix.org",
         "stream_ordering": 8326
      },
      {
         "room_id": "!xYvNcQPhnkrdUmYczI:matrix.org",
         "stream_ordering": 93534
      }
   ],
   "total": 2
}
```

To paginate, check for `next_token` and if present, call the endpoint again
with `from` set to the value of `next_token`. This will return a new page.

If the endpoint does not return a `next_token` then there are no more destinations
to paginate through.

**Parameters**

The following parameters should be set in the URL:

- `destination` - Name of the remote server.

The following query parameters are available:

- `from` - Offset in the returned list. Defaults to `0`.
- `limit` - Maximum amount of destinations to return. Defaults to `100`.
- `dir` - Direction of room order by `room_id`. Either `f` for forwards or `b` for
  backwards. Defaults to `f`.

**Response**

The following fields are returned in the JSON response body:

- `rooms` - An array of objects, each containing information about a room.
  Room objects contain the following fields:
  - `room_id` - string - The ID of the room.
  - `stream_ordering` - integer -  The stream ordering of the most recent
    successfully-sent [PDU](https://element-hq.github.io/synapse/v1.165/usage/administration/understanding_synapse_through_grafana_graphs.html#federation)
    to this destination in this room.
- `next_token`: string representing a positive integer - Indication for pagination. See above.
- `total` - integer - Total number of destinations.

#### Reset connection timeout

Synapse makes federation requests to other homeservers. If a federation request fails,
Synapse will mark the destination homeserver as offline, preventing any future requests
to that server for a "cooldown" period. This period grows over time if the server
continues to fail its responses
([exponential backoff](https://en.wikipedia.org/wiki/Exponential_backoff)).

Admins can cancel the cooldown period with this API.

This API resets the retry timing for a specific remote server and tries to connect to
the remote server again. It does not wait for the next `retry_interval`.
The connection must have previously run into an error and `retry_last_ts`
([Destination Details API](#destination-details-api)) must not be equal to `0`.

The connection attempt is carried out in the background and can take a while
even if the API already returns the http status 200.

The API is:

```
POST /_synapse/admin/v1/federation/destinations/<destination>/reset_connection

{}
```

**Parameters**

The following parameters should be set in the URL:

- `destination` - Name of the remote server.

---

## Client-Server API Extensions

*Source: [`docs/admin_api/client_server_api_extensions.md`](https://github.com/element-hq/synapse/blob/v1.165.0/docs/admin_api/client_server_api_extensions.md) · [rendered](https://element-hq.github.io/synapse/v1.165/admin_api/client_server_api_extensions.html)*

### Client-Server API Extensions

Server administrators can set special account data to change how the Client-Server API behaves for
their clients. Setting the account data, or having it already set, as a non-admin has no effect.

All configuration options can be set through the `io.element.synapse.admin_client_config` global 
account data on the admin's user account. 

Example:
```
PUT /_matrix/client/v3/user/{adminUserId}/account_data/io.element.synapse.admin_client_config
{
    "return_soft_failed_events": true
}
```

#### See soft failed events

Learn more about soft failure from [the spec](https://spec.matrix.org/v1.14/server-server-api/#soft-failure).

To receive soft failed events in APIs like `/sync` and `/messages`, set `return_soft_failed_events`
to `true` in the admin client config. When `false`, the normal behaviour of these endpoints is to
exclude soft failed events.

**Note**: If the policy server flagged the event as spam and that caused soft failure, that will be indicated
in the event's `unsigned` content like so:

```json
{
  "type": "m.room.message",
  "other": "event_fields_go_here",
  "unsigned": {
    "io.element.synapse.soft_failed": true,
    "io.element.synapse.policy_server_spammy": true
  }
}
```

Default: `false`

#### See events marked spammy by policy servers

Learn more about policy servers from [MSC4284](https://github.com/matrix-org/matrix-spec-proposals/pull/4284).

Similar to `return_soft_failed_events`, clients logged in with admin accounts can see events which were
flagged by the policy server as spammy (and thus soft failed) by setting `return_policy_server_spammy_events`
to `true`.

`return_policy_server_spammy_events` may be `true` while `return_soft_failed_events` is `false` to only see
policy server-flagged events. When `return_soft_failed_events` is `true` however, `return_policy_server_spammy_events`
is always `true`.

Events which were flagged by the policy will be flagged as `io.element.synapse.policy_server_spammy` in the
event's `unsigned` content, like so:

```json
{
  "type": "m.room.message",
  "other": "event_fields_go_here",
  "unsigned": {
    "io.element.synapse.soft_failed": true,
    "io.element.synapse.policy_server_spammy": true
  }
}
```

Default: `true` if `return_soft_failed_events` is `true`, otherwise `false`

---
