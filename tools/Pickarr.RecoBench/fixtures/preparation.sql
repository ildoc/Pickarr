-- Synthetic subset of the real schema; no application initialization or migrations.
CREATE TABLE Titles (Id INTEGER PRIMARY KEY);
CREATE TABLE Profiles (Id INTEGER PRIMARY KEY);
CREATE TABLE Ratings (
    ProfileId INTEGER NOT NULL REFERENCES Profiles(Id),
    TitleId INTEGER NOT NULL REFERENCES Titles(Id),
    Value TEXT NOT NULL,
    RatedAt TEXT NOT NULL,
    PRIMARY KEY (ProfileId, TitleId)
);
INSERT INTO Titles VALUES (1), (2), (3), (4), (5), (6), (7), (8);
INSERT INTO Profiles VALUES (1), (2);
INSERT INTO Ratings VALUES
    (1, 1, 'Love', '2026-01-01 00:00:00'),
    (1, 2, 'Like', '2026-01-02 00:00:00'),
    (1, 3, 'Dislike', '2026-01-03 00:00:00'),
    (1, 4, 'NotInterested', '2026-01-04 00:00:00'),
    (2, 1, 'Like', '2026-01-05 00:00:00');
