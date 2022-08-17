namespace Lithiumfrost.Function

open System
open System.IO
open System.Net.Http
open System.Text.Encodings.Web
open System.Web
open Microsoft.Azure.WebJobs
open Microsoft.Extensions.Logging
open FSharp.Data
open FsHttp
open SixLabors.ImageSharp
open SixLabors.ImageSharp.Processing
open SixLabors.ImageSharp.Formats.Jpeg
open Flurl
open Flurl.Http

module sendClimateForecast =

    let getAsync (url: string) =
        async {
            let httpClient =
                new System.Net.Http.HttpClient()

            let! response = httpClient.GetAsync(url) |> Async.AwaitTask
            response.EnsureSuccessStatusCode() |> ignore

            let! content =
                response.Content.ReadAsStreamAsync()
                |> Async.AwaitTask

            return content
        }

    let inline getGchatJsonMessage title subtitle imageuri weatheruri =
        {| cards =
            [ {| header =
                  {| title = $"%s{title}"
                     subtitle = $"%s{subtitle}" |} |}
              :> obj
              {| sections =
                  [ {| widgets =
                        [ {| image =
                              {| imageUrl = $"%s{imageuri}"
                                 onClick = {| openLink = {| url = $"%s{weatheruri}" |} |} |} |} ] |} ] |}
              :> obj ] |}
        |> System.Text.Json.JsonSerializer.Serialize

    [<FunctionName("sendClimateForecastFunction")>]
    let run ([<TimerTrigger("0 0 18 * * 4")>] myTimer: TimerInfo, log: ILogger) =
        let msg =
            $"Weather update function ran at: %A{DateTime.Now}"

        log.LogInformation msg

        let weatherImageUri =
            "https://weather.gc.ca/data/saisons/images/mfe1t_s.gif"

        let weatherImage =
            getAsync weatherImageUri |> Async.RunSynchronously

        let telegramChatId = "-414892076"

        let apiTelegramKey =
            Environment.GetEnvironmentVariable("APIKEY", EnvironmentVariableTarget.Process)

        let apiGoogleKey =
            Environment.GetEnvironmentVariable("GOOGLEKEY", EnvironmentVariableTarget.Process)

        let googleWebhook =
            "https://chat.googleapis.com/v1/spaces/AAAAWDidqi8/messages?key="
            + apiGoogleKey

        let msgContent =
            getGchatJsonMessage
                "Edmonton 1 Month Climate Outlook"
                "Temperature"
                weatherImageUri
                "https://weather.gc.ca/saisons/image_e.html?img=mfe1t_s"

        [ async {
              let! res =
                  Http.AsyncRequest(
                      "https://api.telegram.org/bot"
                      + apiTelegramKey
                      + "/sendPhoto?chat_id="
                      + telegramChatId,
                      body =
                          Multipart(
                              boundary = "b322328f-346b-43e5-ab84-a7f9926fb5a6",
                              parts = [ MultipartItem("photo", "climate_map.gif", weatherImage) ]
                          )
                  )

              do log.LogInformation $"Telegram gave back a status code of: %i{res.StatusCode}"
          }
          async {
              let! response =
                  Http.AsyncRequest(
                      googleWebhook,
                      headers = [ "Content-Type", HttpContentTypes.Json ],
                      body = TextRequest msgContent
                  )

              do log.LogInformation $"Google Chat gave back a status code of: %i{response.StatusCode}"
          } ]
        |> Async.Parallel
        |> Async.Ignore
        |> Async.RunSynchronously



    [<FunctionName("sendPrecipitationForecastFunction")>]
    let runner ([<TimerTrigger("0 0 20 3 * *")>] myTimer: TimerInfo, log: ILogger) =
        let msg =
            $"Weather precipitation update function ran at: %A{DateTime.Now}"

        log.LogInformation msg

        let img =
            http { GET "https://www.weather.gc.ca/data/saisons/images/s123pfe1p_cal.gif" }
            |> Request.send
            |> Response.toStream

        let cropWeatherImage (img: Stream) =
            use img = Image.Load(img)

            let imgHeight =
                if img.Height < 600 then
                    img.Height
                else
                    600

            let imgWidth =
                if img.Width < 635 then
                    img.Height
                else
                    635

            let smallStream = new MemoryStream()

            let jpegEncoder = JpegEncoder(Quality = 75)

            let clone =
                img.Clone (fun ctx ->
                    ctx.Resize(
                        ResizeOptions(
                            Position = AnchorPositionMode.TopLeft,
                            Mode = ResizeMode.Crop,
                            Size = Size(imgWidth, imgHeight)
                        )
                    )
                    |> ignore)

            clone.Save(smallStream, jpegEncoder)
            smallStream

        let telegramChatId = "-414892076"

        let apiTelegramKey =
            Environment.GetEnvironmentVariable("APIKEY", EnvironmentVariableTarget.Process)

        let caption =
            "[Environment Canada](https://www.weather.gc.ca/saisons/prob_e.html) _Probabilistic 3\-Month Precipitation Map_"

        let res =
            task {
                let image = cropWeatherImage img
                image.Seek(0, SeekOrigin.Begin) |> ignore

                let! res =
                    $"https://api.telegram.org/bot%s{apiTelegramKey}/sendPhoto"
                        .SetQueryParams(
                            {| chat_id = telegramChatId
                               caption = caption
                               parse_mode = "MarkdownV2" |}
                        )
                        .PostMultipartAsync(fun mp ->
                            mp.AddFile("photo", image, "precip_map.jpg", "image/jpeg")
                            |> ignore)

                return res.ResponseMessage
            }

        res.Result |> string |> log.LogInformation
