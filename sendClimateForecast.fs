namespace Lithiumfrost.Function

open System
open System.Text.Encodings.Web
open Microsoft.Azure.WebJobs
open Microsoft.Azure.WebJobs.Host
open Microsoft.Extensions.Logging
open FSharp.Data

module sendClimateForecast =
    
    let getAsync (url:string) = 
        async {
            let httpClient = new System.Net.Http.HttpClient()
            let! response = httpClient.GetAsync(url) |> Async.AwaitTask
            response.EnsureSuccessStatusCode () |> ignore
            let! content = response.Content.ReadAsStreamAsync() |> Async.AwaitTask
            return content
        }
    
    [<FunctionName("sendClimateForecastFunction")>]
    let run([<TimerTrigger("0 0 18 * * 4")>]myTimer: TimerInfo, log: ILogger) =
        let msg = sprintf "Weather update function ran at: %A" DateTime.Now
        log.LogInformation msg
        
        let weatherImage = getAsync "https://weather.gc.ca/data/saisons/images/mfe1t_s.gif" |> Async.RunSynchronously
        
        let apiKey = Environment.GetEnvironmentVariable "APIKEY" 
        
        Http.Request("https://api.telegram.org/bot" + apiKey + "/sendPhoto?chat_id=" +
                     System.Web.HttpUtility.UrlEncode("-414892076"),
                     body = Multipart(
                                         boundary = "b322328f-346b-43e5-ab84-a7f9926fb5a6",
                                         parts = [
                                             MultipartItem("photo", "climate_map.gif", weatherImage)
                                         ]
                                     )) 
             |> (fun res -> log.LogInformation (sprintf "Telegram gave back a status code of: %i" res.StatusCode))
        