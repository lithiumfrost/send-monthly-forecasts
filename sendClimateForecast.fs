namespace Lithiumfrost.Function

open System
open System.Net.Mime
open System.Text.Encodings.Web
open Microsoft.Azure.WebJobs
open Microsoft.Azure.WebJobs.Host
open Microsoft.Extensions.Logging
open FSharp.Data

module sendClimateForecast =

    let getAsync (url: string) =
        async {
            let httpClient = new System.Net.Http.HttpClient()
            let! response = httpClient.GetAsync(url) |> Async.AwaitTask
            response.EnsureSuccessStatusCode() |> ignore
            let! content = response.Content.ReadAsStreamAsync() |> Async.AwaitTask
            return content
        }

    [<FunctionName("sendClimateForecastFunction")>]
    let run ([<TimerTrigger("0 0 18 * * 4")>] myTimer: TimerInfo, log: ILogger) =
        let msg = sprintf "Weather update function ran at: %A" DateTime.Now
        log.LogInformation msg

        let weatherImageUri = "https://weather.gc.ca/data/saisons/images/mfe1t_s.gif"
        let weatherImage = getAsync weatherImageUri |> Async.RunSynchronously
        let apiTelegramKey = Environment.GetEnvironmentVariable("APIKEY", EnvironmentVariableTarget.Process)

        let apiGoogleKey = Environment.GetEnvironmentVariable("GOOGLEKEY", EnvironmentVariableTarget.Process)
        let googleWebhook =
            "https://chat.googleapis.com/v1/spaces/AAAAWDidqi8/messages?key=" + apiGoogleKey

        let msgContent =
            sprintf """{
                                    "cards": [
                                      {
                                        "header": {
                                          "title": "Edmonton 1 Month Climate Outlook",
                                          "subtitle": "Temperature"
                                        },
                                        "sections": [
                                          {
                                            "widgets": [
                                              {
                                                "image": {
                                                  "imageUrl": "%s",
                                                  "onClick": {
                                                    "openLink": {
                                                      "url": "https://weather.gc.ca/saisons/image_e.html?img=mfe1t_s"
                                                    }
                                                  }
                                                }
                                              }
                                            ]
                                          }
                                        ]
                                      }
                                    ]
                                  }
                                  """ weatherImageUri

        [ async {
            let! res =
                Http.AsyncRequest
                    ("https://api.telegram.org/bot" + apiTelegramKey + "/sendPhoto?chat_id="
                     + System.Web.HttpUtility.UrlEncode("-414892076"),
                     body =
                         Multipart
                             (boundary = "b322328f-346b-43e5-ab84-a7f9926fb5a6",
                              parts = [ MultipartItem("photo", "climate_map.gif", weatherImage) ]))
            do log.LogInformation(sprintf "Telegram gave back a status code of: %i" res.StatusCode) }
          async {
              let! response =
                  Http.AsyncRequest
                      (googleWebhook, headers = [ "Content-Type", HttpContentTypes.Json ],
                       body = TextRequest msgContent)
              do log.LogInformation(sprintf "Google Chat gave back a status code of: %i" response.StatusCode) } ]
        |> Async.Parallel
        |> Async.Ignore
        |> Async.RunSynchronously
