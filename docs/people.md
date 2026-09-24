# People

A `Person` is someone who **appears in photos** (tagged through `photo_people`) or **whose camera
took them** (`photos.camera_owner_id`). Source: `src/NpApi.Api/Features/People/`, plus
`Features/Photos/PhotoPerson.cs` for the link.

## Model

```
people                         photo_people                        photos
------                         ------------                        ------
id (uuid, v7)          ◄────── person_id  ┐ PK (photo_id,          id
first_name (required)          photo_id ──┘     person_id) ──────► camera_owner_id ──► people.id
middle_name?                   tagged_by (Auth0 sub)               ...
last_name?                     tagged_at
created_at
```

| Rule | How it's enforced |
| --- | --- |
| A photo has at most **one** camera owner | A nullable FK column on `photos`, not a row in the link table |
| A photo can show **many** people, each at most once | `photo_people` with primary key `(photo_id, person_id)` |
| Deleting a person removes their tags | `photo_people.person_id` → `ON DELETE CASCADE` |
| Deleting a photo removes its tags | `photo_people.photo_id` → `ON DELETE CASCADE` |
| A camera owner can't be deleted | `photos.camera_owner_id` → `ON DELETE RESTRICT`. The API returns **409**; reassign the photos first. |
| "All photos of X" stays fast | Index on `photo_people.person_id` (the primary key starts with `photo_id`) |
| First name can't be blank | Check constraint `btrim(first_name) <> ''` |

The camera owner can also appear in the photo: 52 of yellowstone's 477 photos tag their camera owner.
The two relationships are independent.

## Design decisions

- **Identified by id, never by name.** There's no unique index on names, because two real people can
  share one. Frontends should pick people from a list rather than type names. Duplicates can be
  merged later if they happen.
- **People are linked, not free-text tags.** yellowstone stores `people: ["Nate"]` as strings, so a
  rename or typo splits a person, and two "Amy"s merge. `Photo.Tags` (`text[]`) stays for
  categories such as wildlife or sites, and **never** holds people.
- **"Camera owner" rather than "photographer".** EXIF records the device, not who pressed the
  shutter, so the owner of the phone is what we can actually know. A separate "taken by" can come
  later if it matters.
- **A person isn't a user (yet).** Nate is both a person in photos and an Auth0 user. A nullable,
  unique `user_id` on `people` can link them later ("photos of me", untagging yourself).
- **Room for face regions.** "Who's where" boxes can become optional columns on `photo_people`
  without changing the key.

## API

| Route | Permission | |
| --- | --- | --- |
| `GET /api/v1/people` | `read:people` | Sorted by first, then last name |
| `GET /api/v1/people/{id}` | `read:people` | |
| `POST /api/v1/people` | `write:people` | `{ "firstName", "middleName"?, "lastName"? }` → 201 |
| `PUT /api/v1/people/{id}` | `write:people` | Replaces the names |
| `DELETE /api/v1/people/{id}` | `write:people` | 204. **409** if they own photos. Their tags are removed. |

Names are trimmed. Blank middle and last names are stored as `null`. Each name allows 100 characters.
Responses include `displayName` ("Laura Ann Paxton"), computed rather than stored.

Photos reference people by id: upload with `cameraOwnerId`. Photo responses include
`cameraOwnerId` plus `cameraOwner` (the display name, matching yellowstone's field).

**Not built yet (tagging session):** adding and removing tags
(`POST/DELETE /api/v1/photos/{id}/people/{personId}`), people in photo responses, and filtering
photos by person. The `photo_people` table and relationships are already in the model. Load a list
of photos with their people in one query (`Include`), not one query per photo.

## Importing the existing people

yellowstone's metadata lists seven people by first name (Nate, Laura, Greg, Nancy, Travis, Amy,
Kennedy). An import would create each once, then map `cameraOwner` and `people[]` strings to their ids.

## Privacy

These are real people, including children, and their names appear in API responses. Reads require
`read:people`. Consider this before exposing people publicly.
