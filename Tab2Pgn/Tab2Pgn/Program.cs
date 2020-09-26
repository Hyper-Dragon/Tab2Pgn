using ChessLib.Data;
using ChessLib.Data.Helpers;
using ChessLib.Parse.PGN;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TabToPgn
{
    internal class Program
    {
        private readonly static string[] RANKS = { "1", "2", "3", "4", "5", "6", "7", "8" };
        private readonly static string[] FILES = { "a", "b", "c", "d", "e", "f", "g", "h" };

        private static async Task Main(string[] args)
        {
            //C:\\Dropbox\\ChessStats\\RepSheet\\RepWhite.tsv

            string fileIn = args[0];
            string[] lines = System.IO.File.ReadAllLines(fileIn);
            string preParsedPgn = "";

            if (fileIn.EndsWith("tsv", StringComparison.OrdinalIgnoreCase))
            {
                List<(string title, string eco, int ply, string moves)> formatedMoves = BuildMoveLines(lines);
                preParsedPgn = BuildPreParsedPgn(formatedMoves);
            }
            else if (fileIn.EndsWith("pgn", StringComparison.OrdinalIgnoreCase))
            {
                preParsedPgn = File.ReadAllText(fileIn);
            }
            else
            {
                Environment.Exit(-1);
            }

            IEnumerable<Game<ChessLib.Data.MoveRepresentation.MoveStorage>> parsedGames = await ParseAndValidatePgn(preParsedPgn).ConfigureAwait(false);
            var twentyMovesFile = ValidateMoves(fileIn, parsedGames);
            var (LastMoveNameList, MoveLines, MaxWidth) = BuildMoveImageData(parsedGames, fileIn.Contains("WHITE", StringComparison.OrdinalIgnoreCase));

            blah("AllBlack", LastMoveNameList, MoveLines, MaxWidth);

            //foreach (var tline in MoveLines.Where(x => x.Values.Count > 1 && !string.IsNullOrEmpty(x.Values[1].Item1)) )
            //{
            //    blah(tline.Values[0].Item1, LastMoveNameList, MoveLines, MaxWidth);
            //}            

            using (FileStream fs = File.Create($"C:\\Dropbox\\ChessStats\\TwmBlackOpenings.twm"))
            {
                await JsonSerializer.SerializeAsync(fs, twentyMovesFile).ConfigureAwait(false);
            }


            DisplayPgn(parsedGames);

        }

        const int BOARD_SIZE = 150;

        private static (SortedList<string, string> LastMoveNameList, List<SortedList<string, (string, string, Image, string)>> MoveLines, int MaxWidth) BuildMoveImageData(IEnumerable<Game<ChessLib.Data.MoveRepresentation.MoveStorage>> parsedGames, bool isFromWhitesPerspective = true)
        {
            const string BOARD_DOWNLOAD_SIZE = "0";
            const string BOARD_URL_START = @"https://www.chess.com/dynboard?board=green&fen=";
            const string BOARD_URL_OPT = @"&piece=space&size=" + BOARD_DOWNLOAD_SIZE;
            const string BOARD_FEN = @"rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR";

            string IS_BOARD_FLIPPED = isFromWhitesPerspective ? "" : "&flip=true";

            //Download initial position
            using var webClient = new WebClient();
            using var startBoardImgStream = new MemoryStream(webClient.DownloadData($"{BOARD_URL_START}{BOARD_FEN}{BOARD_URL_OPT}{IS_BOARD_FLIPPED}"));
            using var startBoardBmp = new Bitmap(Image.FromStream(startBoardImgStream));
            Bitmap startBoardresizedBmp = new Bitmap(startBoardBmp, new Size(BOARD_SIZE, BOARD_SIZE));

            SortedList<string, string> lastMoveNameList = new();

            int moveCount = 0;
            int maxWidth = 0;

            List<SortedList<string, (string, string, Image, string)>> moveLines = new();
            moveLines.Add(new SortedList<string, (string, string, Image, string)>());
            moveLines[0].Add(BOARD_FEN, ("", BOARD_FEN, startBoardresizedBmp, ""));

            foreach (Game<ChessLib.Data.MoveRepresentation.MoveStorage> game in parsedGames)
            {
                Image bmp = null;
                string moveKey = BOARD_FEN;
                moveCount = 0;

                while (game.HasNextMove)
                {
                    game.TraverseForward();

                    string[] fenSplit = game.CurrentFEN.Split(" ");
                    string gameKey = $"{fenSplit[0]}";
                    moveKey += gameKey;

                    if (moveLines.Count <= ++moveCount) { moveLines.Add(new SortedList<string, (string, string, Image, string)>()); }

                    if (!moveLines[moveCount].ContainsKey($"{moveKey}"))
                    {
                        using (HttpClient httpClient = new HttpClient())
                        {
                            System.Console.WriteLine($"{gameKey}");
                            var responseTask = httpClient.GetStreamAsync(new Uri($"{BOARD_URL_START}{gameKey}{BOARD_URL_OPT}{IS_BOARD_FLIPPED}"));
                            responseTask.Wait(System.Threading.Timeout.Infinite);
                            bmp = Bitmap.FromStream(responseTask.Result);
                        }


                        Bitmap resizedBmp = new Bitmap(bmp, new Size(BOARD_SIZE, BOARD_SIZE));
                        moveLines[moveCount].Add($"{moveKey}", (game.CurrentMoveNode.Value.SAN, $"{gameKey}", resizedBmp, game.CurrentMoveNode.Value.Comment));
                    }

                    if (moveCount + 1 < moveLines.Count)
                    {
                        int addedCount = moveLines[moveCount + 1].Where(x => x.Key.StartsWith(moveKey, StringComparison.OrdinalIgnoreCase)).Count();
                        if (addedCount >= 1)
                        {
                            moveLines[moveCount].Add($"{moveKey}{addedCount}", ("", "", null, ""));
                        }
                    }

                    maxWidth = Math.Max(maxWidth, moveLines[moveCount].Count);

                    if (!game.HasNextMove && game.TagSection.ContainsKey("Opening"))
                    {
                        lastMoveNameList.Add(moveKey, game.TagSection["Opening"]);
                    }
                }

                game.GoToInitialState();
            }

            for (int loopY = 1; loopY < (moveLines.Count - 1); loopY++)
            {
                for (int loopX = 0; loopX < moveLines[loopY].Count; loopX++)
                {
                    if (moveLines[loopY].Values[loopX].Item3 != null)
                    {
                        int addedCount = moveLines[loopY + 1].Where(x => x.Key.StartsWith(moveLines[loopY].Keys[loopX], StringComparison.OrdinalIgnoreCase)).Count();

                        if (addedCount == 0)
                        {
                            for (int loopInnerY = loopY + 1; loopInnerY < moveLines.Count; loopInnerY++)
                            {
                                moveLines[loopInnerY].Add($"{moveLines[loopY].Keys[loopX]}{addedCount}", ("", "", null, ""));
                            }
                        }
                    }
                }

                maxWidth = Math.Max(maxWidth, moveLines[loopY].Count);
            }

            return (lastMoveNameList, moveLines, maxWidth);
        }

        private static void blah(string startMove, SortedList<string, string> lastMoveNameList, List<SortedList<string, (string, string, Image, string)>> moveLines, int maxWidth) {
            const string BKG_URL = @"https://images.chesscomfiles.com/uploads/v1/theme/101328-0.caa989e5.jpeg";
            const int BOX_WIDTH = 62;
            const int BOX_HEIGHT = 14;
            const float FONT_SIZE = 9f;
            const int SPACER_SIZE_X = 16;
            const int SPACER_SIZE_Y = 30;
            const int BLOCK_SIZE_X = BOARD_SIZE + SPACER_SIZE_X;
            const int BLOCK_SIZE_Y = BOARD_SIZE + SPACER_SIZE_Y;
            const int STRIPE_TXT_OFFSET = 16;
            const float CONNECT_SIZE = 2.0f;

            // Create font/brush/pen.
            using Font drawFont = new Font(FontFamily.GenericSansSerif, FONT_SIZE);
            using Brush drawBrush = new SolidBrush(Color.FromArgb(255, 255, 255, 255));
            using Brush moveBkgBrush = new SolidBrush(Color.FromArgb(235, 200, 0, 0));
            using Pen orangePen = new Pen(Brushes.Orange) { Width = CONNECT_SIZE };

            using var image = new Bitmap((maxWidth * BLOCK_SIZE_X) + SPACER_SIZE_X, moveLines.Count * BLOCK_SIZE_Y, PixelFormat.Format32bppArgb);
            using var graphics = Graphics.FromImage(image);

            graphics.CompositingQuality = CompositingQuality.HighQuality;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.CompositingMode = CompositingMode.SourceOver;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = TextRenderingHint.AntiAlias;

            graphics.Clear(Color.Black);

            WebClient webClient = new();
            using var bkgImgStream = new MemoryStream(webClient.DownloadData(BKG_URL));
            Image bkgImage = Image.FromStream(bkgImgStream);
            graphics.DrawImage(bkgImage, 0, 0, image.Width, image.Height);

            for (int loopY = 0; loopY < moveLines.Count; loopY++)
            {
                //if(moveLines[loopY].Values.Count > 1 && moveLines[loopY].Values[1].Item1 != startMove)
                //{
                //    continue;
                //}

                var moveLine = moveLines[loopY].OrderBy(x => x.Key).ToArray();

                for (int loopX = 0; loopX < moveLine.Length; loopX++)
                {
                    //Draw Connector
                    if (loopY + 1 < moveLines.Count)
                    {
                        for (int loopNextRowX = 0; loopNextRowX < moveLines[loopY + 1].Count; loopNextRowX++)
                        {
                            if (moveLines[loopY + 1].Keys[loopNextRowX].Contains(moveLines[loopY].Keys[loopX], StringComparison.OrdinalIgnoreCase))
                            {
                                if (moveLines[loopY + 1].Values[loopNextRowX].Item3 != null)
                                {
                                    graphics.DrawLine(orangePen,
                                                      (SPACER_SIZE_X) + (loopX * BLOCK_SIZE_X) + (SPACER_SIZE_X / 2) + (BOARD_SIZE / 2),
                                                      (loopY * BLOCK_SIZE_Y) + (SPACER_SIZE_Y / 2) + (BOARD_SIZE),
                                                      (SPACER_SIZE_X) + (loopX * BLOCK_SIZE_X) + (SPACER_SIZE_X / 2) + (BOARD_SIZE / 2),
                                                      (loopY * BLOCK_SIZE_Y) + (SPACER_SIZE_Y / 2) + (BOARD_SIZE) + (SPACER_SIZE_Y / 2));

                                    graphics.DrawLine(orangePen,
                                                      (SPACER_SIZE_X) + (loopNextRowX * BLOCK_SIZE_X) + (SPACER_SIZE_X / 2) + (BOARD_SIZE / 2),
                                                      (loopY * BLOCK_SIZE_Y) + (SPACER_SIZE_Y / 2) + (BOARD_SIZE) + (SPACER_SIZE_Y / 2),
                                                      (SPACER_SIZE_X) + (loopNextRowX * BLOCK_SIZE_X) + (SPACER_SIZE_X / 2) + (BOARD_SIZE / 2),
                                                      ((loopY + 1) * BLOCK_SIZE_Y) + (SPACER_SIZE_Y / 2));

                                    graphics.DrawLine(orangePen,
                                                      (SPACER_SIZE_X) + (loopX * BLOCK_SIZE_X) + (SPACER_SIZE_X / 2) + (BOARD_SIZE / 2),
                                                      (loopY * BLOCK_SIZE_Y) + (SPACER_SIZE_Y / 2) + (BOARD_SIZE) + (SPACER_SIZE_Y / 2),
                                                      (SPACER_SIZE_X) + (loopNextRowX * BLOCK_SIZE_X) + (SPACER_SIZE_X / 2) + (BOARD_SIZE / 2),
                                                      ((loopY + 1) * BLOCK_SIZE_Y));
                                }
                            }
                        }
                    }
                }
            }


            for (int loopY = 0; loopY < moveLines.Count; loopY++)
            {
                //if (moveLines[loopY].Values.Count > 1 && moveLines[loopY].Values[1].Item1 != startMove)
                //{
                //    continue;
                //}

                var moveLine = moveLines[loopY].OrderBy(x => x.Key).ToArray();

                for (int loopX = 0; loopX < moveLine.Length; loopX++)
                {
                    if (lastMoveNameList.ContainsKey(moveLines[loopY].Keys[loopX]))
                    {
                        using var drawFormat = new System.Drawing.StringFormat() { FormatFlags = StringFormatFlags.DirectionVertical };
                        var stringSize = graphics.MeasureString(lastMoveNameList[moveLines[loopY].Keys[loopX]], drawFont);

                        for (float txtLoop = (SPACER_SIZE_Y / 2) + BLOCK_SIZE_Y; txtLoop < image.Height; txtLoop += (stringSize.Width + 30))
                        {
                            graphics.DrawString(lastMoveNameList[moveLines[loopY].Keys[loopX]],
                                                drawFont,
                                                drawBrush,
                                                (SPACER_SIZE_X) + ((loopX * BLOCK_SIZE_X) + (SPACER_SIZE_X / 2)) - STRIPE_TXT_OFFSET,
                                                txtLoop,
                                                drawFormat);
                        }

                        //lastMoveNameList.Remove(moveLines[loopY].Keys[loopX]);
                    }

                    if (loopY + 1 < moveLines.Count)
                    {
                        bool isWhite = true;
                        for (int loopNextRowX = 0, moveNum = 1; loopNextRowX < moveLines[loopY + 1].Count; loopNextRowX++, moveNum = !isWhite ? moveNum + 1 : moveNum, isWhite = !isWhite)
                        {
                            if (moveLines[loopY + 1].Keys[loopNextRowX].Contains(moveLines[loopY].Keys[loopX], StringComparison.OrdinalIgnoreCase))
                            {
                                if (moveLines[loopY + 1].Values[loopNextRowX].Item3 != null)
                                {
                                    graphics.FillRectangle(moveBkgBrush,
                                                           (SPACER_SIZE_X) + ((loopNextRowX * BLOCK_SIZE_X) + (SPACER_SIZE_X / 2) + (BOARD_SIZE / 2)) - (BOX_WIDTH / 2),
                                                           ((loopY * BLOCK_SIZE_Y) + (SPACER_SIZE_Y / 2) + (BOARD_SIZE) + (SPACER_SIZE_Y / 2)) - (BOX_HEIGHT / 2),
                                                           BOX_WIDTH,
                                                           BOX_HEIGHT);

                                    graphics.DrawString($"{(int)Math.Round((loopY + 1) / 2d, MidpointRounding.AwayFromZero)}.{((loopY + 1) % 2d != 0 ? "" : "..")} {moveLines[loopY + 1].Values[loopNextRowX].Item1}",
                                                        drawFont,
                                                        drawBrush,
                                                        (SPACER_SIZE_X) + ((loopNextRowX * BLOCK_SIZE_X) + (SPACER_SIZE_X / 2) + (BOARD_SIZE / 2)) - (BOX_WIDTH / 2) + 1,
                                                        ((loopY * BLOCK_SIZE_Y) + (SPACER_SIZE_Y / 2) + (BOARD_SIZE) + (SPACER_SIZE_Y / 2)) - (BOX_HEIGHT / 2) + 1);
                                }
                            }
                        }
                    }

                    //Draw Board
                    if (moveLine[loopX].Value.Item3 != null)
                    {
                        graphics.DrawImage(moveLine[loopX].Value.Item3,
                                           (SPACER_SIZE_X) + (loopX * BLOCK_SIZE_X) + (SPACER_SIZE_X / 2),
                                           (loopY * BLOCK_SIZE_Y) + (SPACER_SIZE_Y / 2));
                    }
                }
            }

            image.Save($"C:\\Dropbox\\ChessStats\\resized_{startMove}.png", ImageFormat.Png);
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1303:Do not pass literals as localized parameters", Justification = "Will never be localized")]
        private static List<TwentyMovesOpeningFile> ValidateMoves(string fileIn, IEnumerable<Game<ChessLib.Data.MoveRepresentation.MoveStorage>> parsedGames)
        {
            List<TwentyMovesOpeningFile> twentyMovesOpenings = new();

            ChessLib.Data.Types.Enums.Color? repForSide = null;
            SortedDictionary<string, (string pgnEvent, string move)> fenList = new SortedDictionary<string, (string pgnEvent, string move)>();

            if (fileIn.Contains("WHITE", StringComparison.InvariantCultureIgnoreCase))
            {
                repForSide = ChessLib.Data.Types.Enums.Color.White;
            }
            else if (fileIn.Contains("BLACK", StringComparison.InvariantCultureIgnoreCase))
            {
                repForSide = ChessLib.Data.Types.Enums.Color.Black;
            }

            foreach (Game<ChessLib.Data.MoveRepresentation.MoveStorage> game in parsedGames)
            {
                var twentyMovesGame = new TwentyMovesOpeningFile() { Opening = "test" };
                twentyMovesOpenings.Add(twentyMovesGame);

                while (game.HasNextMove)
                {
                    game.TraverseForward();

                    twentyMovesGame.Moves.Add(new TwentyMovesMove() { San = game.CurrentMoveNode.Value.SAN,
                                                                      FromSquare = $"{FILES[game.CurrentMoveNode.Value.SourceIndex.FileFromIdx()]}{RANKS[game.CurrentMoveNode.Value.SourceIndex.RankFromIdx()]}",
                                                                      ToSquare = $"{FILES[game.CurrentMoveNode.Value.DestinationIndex.FileFromIdx()]}{RANKS[game.CurrentMoveNode.Value.DestinationIndex.RankFromIdx()]}",
                                                                      MoveNumber = 0
                    });


                    if (repForSide != null && game.Board.ActivePlayer == repForSide)
                    {
                        if (game.HasNextMove)
                        {
                            string[] fenSplit = game.CurrentFEN.Split(" ");
                            string gameKey = $"{fenSplit[0]} {fenSplit[1]} {fenSplit[2]}";

                            if (fenList.ContainsKey(gameKey))
                            {
                                if (fenList[gameKey].move != game.NextMoveNode.Value.ToString())
                                {
                                    Console.WriteLine($"WARN: Multiple Candidate Moves Detected");
                                    Console.WriteLine($"      {fenList[gameKey].pgnEvent}");
                                    Console.WriteLine($"      {game.TagSection["Event"]} ({game.Board.ActivePlayer} Move {game.Board.FullmoveCounter})");
                                    Console.WriteLine($"      {gameKey,-75} -> {fenList[gameKey].move}/{game.NextMoveNode.Value}");
                                    Console.WriteLine("");
                                }
                            }
                            else
                            {
                                fenList.Add(gameKey, ($"{game.TagSection["Event"]} ({game.Board.ActivePlayer} Move {game.Board.FullmoveCounter})", game.NextMoveNode.Value.ToString()));
                            }
                        }
                    }
                }

                game.GoToInitialState();
            }

            Console.WriteLine("");

            return twentyMovesOpenings;
        }

        private static List<(string title, string eco, int ply, string moves)> BuildMoveLines(string[] lines)
        {
            string currentTitle = "";
            string currentECO = "";
            List<string> currentMoves = null;
            List<(string title, string eco, int ply, string moves)> formatedMoves = new List<(string title, string eco, int ply, string moves)>();

            foreach (string line in lines)
            {
                switch (line.Substring(0, 5).ToUpperInvariant())
                {
                    case "TITLE":
                        currentECO = (line.Split("\t"))[1];
                        currentTitle = (line.Split("\t"))[2];
                        break;
                    case "MOVES":
                        currentMoves = (line.Split("\t")).Select(x => x)
                                                         .Where(x => !string.IsNullOrEmpty(x))
                                                         .Skip(1).ToList();

                        formatedMoves.Add((currentTitle, currentECO, currentMoves.Count, TabToPgnFormat(currentMoves)));
                        break;
                    case "COMME":
                        formatedMoves[^1] = (currentTitle, currentECO, currentMoves.Count, TabToPgnFormat(currentMoves, (line.Split("\t")).Skip(1).ToList()));
                        break;
                    default:
                        break;
                }
            }

            return formatedMoves;
        }

        private static string BuildPreParsedPgn(List<(string title, string eco, int ply, string moves)> formatedMoves)
        {
            DateTime todaysDate = DateTime.UtcNow;
            StringBuilder joinedPgn = new StringBuilder();

            foreach ((string title, string eco, int ply, string moves) in formatedMoves)
            {
                string head = $"[Event \"{title.Replace("\"", "", StringComparison.InvariantCultureIgnoreCase)}\"]\r\n" +
                              $"[Site \"Tab2Pgn\"]\r\n" +
                              $"[UTCDate \"{todaysDate.ToString("yyyy.mm.dd", CultureInfo.CurrentCulture)}\"]\r\n" +
                              $"[UTCTime \"{todaysDate.ToString("hh:mm:ss", CultureInfo.CurrentCulture)}\"]\r\n" +
                              $"[Variant \"Standard\"]\r\n" +
                              $"[Round \"?\"]\r\n" +
                              $"[White \"?\"]\r\n" +
                              $"[Black \"?\"]\r\n" +
                              $"[Result \"*\"]\r\n" +
                              $"[Annotator \"?\"]\r\n" +
                              $"[ECO \"{eco.Replace("\"", "", StringComparison.InvariantCultureIgnoreCase)}\"]\r\n" +
                              $"[Opening \"{title.Replace("\"", "", StringComparison.InvariantCultureIgnoreCase)}\"]\r\n" +
                              $"[PlyCount \"{ply}\"]\r\n" +
                              $"\r\n" +
                              $"{moves}";

                joinedPgn.Append(head);
            }

            return joinedPgn.ToString();
        }

        private static void DisplayPgn(IEnumerable<Game<ChessLib.Data.MoveRepresentation.MoveStorage>> parsedGames)
        {
            foreach (Game<ChessLib.Data.MoveRepresentation.MoveStorage> game in parsedGames)
            {
                Console.WriteLine(game.ToString());
            }
        }

        private static async Task<IEnumerable<Game<ChessLib.Data.MoveRepresentation.MoveStorage>>> ParseAndValidatePgn(string preParsedPgn)
        {
            PGNParser pgnParser = new ChessLib.Parse.PGN.PGNParser();
            System.Collections.Generic.IEnumerable<Game<ChessLib.Data.MoveRepresentation.MoveStorage>> parsedPgn = await pgnParser.GetGamesFromPGNAsync(preParsedPgn.ToString(CultureInfo.InvariantCulture)).ConfigureAwait(false);
            return parsedPgn;
        }

        private static string TabToPgnFormat(List<string> currentMoves, List<string> comments = null)
        {
            StringBuilder lineOut = new StringBuilder();
            for (int loop = 0, moveCount = 1; loop < currentMoves.Count; loop++, moveCount = ((loop % 2) == 0) ? moveCount : moveCount + 1)
            {
                string includedComment = (comments == null) ? " " : ((string.IsNullOrEmpty(comments[loop])) ? " " : " {" +
                                         $"{comments[loop]}" + "} " +
                                         (((loop % 2) == 0) ? $"{moveCount}..." : ""));

                if ((loop % 2) == 0)
                {
                    lineOut.Append($"{moveCount}.{currentMoves[loop]}{includedComment}");
                }
                else
                {
                    lineOut.Append($"{currentMoves[loop]}{includedComment}");
                }
            }

            lineOut.Append("*\r\n\r\n");
            return lineOut.ToString();
        }
    }


    public class TwentyMovesOpeningFile
    {
        public string Opening { get; set; }
        public List<TwentyMovesMove> Moves { get; } = new List<TwentyMovesMove>();

        public TwentyMovesOpeningFile() { }
    }

    public class TwentyMovesMove
    {
        public int MoveNumber { get; set; }
        public string San { get; set; }
        public string FromSquare { get; set; }
        public string ToSquare { get; set; }

        public TwentyMovesMove() { }
    }
}
