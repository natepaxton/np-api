# Places

A `Place` is a named location a photo can belong to, such as "Gardiner, Montana, US". Source:
`src/NpApi.Api/Features/Places/`. Place types (parks, NPS sites, attractions) come later.

## Model

| Field | Type | Notes |
| --- | --- | --- |
| `Id` | `uuid` (v7) | |
| `City`, `StateProvince` | `varchar(100)?` | Optional. Trimmed, and blanks are stored as `null`. |
| `CountryCode` | `char(2)?` → `countries.code` | Optional ISO 3166-1 alpha-2 code (see below) |
| `Latitude`, `Longitude` | `double?` | A **representative point** (town or park centre). Both or neither, range-checked. |
| `CreatedAt` | `timestamptz` | |

A place must have **at least one** of city, state/province, country code or a point (check constraint
`ck_places_not_empty`, plus API validation). `displayName` joins the names that are present
("West Yellowstone, US", using the country code), or shows the coordinates for a point-only place.

## Countries

Countries are **ISO 3166-1 alpha-2 codes**, so "USA", "United States" and "US" can't split one
country three ways. Two things define the supported set, and they're kept identical:

- **`CountryCode` enum** (`US`, `CA`, `MX`). The member names *are* the codes and are stored as text,
  so never rename a member.
- **`countries` table** (`code char(2)` primary key, `name`), seeded from the enum by the migration.
  `places.country_code` is a foreign key to it, so the database rejects any other code even if the
  API is bypassed.

| Code | Name |
| --- | --- |
| `CA` | Canada |
| `MX` | Mexico |
| `US` | United States |

**Adding a country:** add a member to `CountryCode` and its name to `Country.Names`, then run
`dotnet ef migrations add Add<Country>`. The migration inserts the new row. A test fails if the enum,
the names, and the seeded table ever disagree.

The API takes `country` as a code, case-insensitive (`"us"` works). Anything else (`"USA"`, `"ZZ"`, a
number) gets a 400 listing the allowed codes. Responses return `country` (the code) and
`countryName`. `GET /api/v1/countries` lists the supported countries for pickers.

A photo links to **at most one** place: nullable `photos.place_id`. Deleting a place **unlinks** its
photos (`ON DELETE SET NULL`); the photos stay.

## Two points: the photo's and the place's

Photos keep their own `lat`/`lng`/`locationSource`, which is the exact spot the camera recorded (or
someone set). A place's point is a representative spot for an area. They're kept separate on purpose:

- **Precision.** Every upload with GPS has its own point a few meters from the last. Storing
  coordinates only on places would mean one place per photo, or snapping photos to nearby places
  and losing the exact point.
- **No fuzzy matching on upload.** Deciding whether a new point "is" an existing place would need
  radius rules that are easy to get wrong.
- **Fallback for photos without GPS.** Assign a place, and the map can show the photo there.

Map rule for frontends: **use the photo's `lat`/`lng`; if null, use `place.lat`/`place.lng`; if both
are null, the photo isn't on the map.** When using the place's point, style the marker as approximate.

## API

| Route | Permission | |
| --- | --- | --- |
| `GET /api/v1/countries` | `read:places` | Supported countries (`code`, `name`), sorted by name |
| `GET /api/v1/places` | `read:places` | Sorted by country code, state/province, city |
| `GET /api/v1/places/{id}` | `read:places` | |
| `POST /api/v1/places` | `write:places` | `{ city?, stateProvince?, country? (code), lat?, lng? }` → 201 |
| `PUT /api/v1/places/{id}` | `write:places` | Replaces all fields (omitting `lat`/`lng` clears the point) |
| `DELETE /api/v1/places/{id}` | `write:places` | 204. Photos are unlinked, not deleted. |

Photo upload takes an optional `placeId`. An unknown id gets 400, checked before anything goes to
Cloudinary. Photo responses include `place` (the full place, or `null`).

## Later

- **Place types** (city, park, NPS site, attraction). yellowstone's `npsSites` (15 parks and
  historic sites) and `attractions` tags are natural candidates to become places.
- **Hierarchy.** "Old Faithful" is in "Yellowstone", which is in "Wyoming". A nullable `parent_id`
  would allow rolling up or filtering by region.
- **Reverse geocoding.** City, state and country could be filled from a point (e.g. OpenStreetMap
  Nominatim, which is free but has a usage policy and rate limits, or a paid geocoder).
