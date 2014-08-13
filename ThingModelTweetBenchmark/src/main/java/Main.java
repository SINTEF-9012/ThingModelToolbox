import org.thingmodel.*;
import org.thingmodel.Location;
import org.thingmodel.builders.BuildANewThing;
import org.thingmodel.builders.BuildANewThingType;
import twitter4j.*;
import websockets.Client;

import java.io.IOException;
import java.net.URI;
import java.net.URISyntaxException;

public class Main {
    public static void main(String[] args) throws TwitterException, IOException, URISyntaxException {
        final ThingType tweetType = BuildANewThingType.Named("tweet")
                .ContainingA.LocationLatLng()
                .AndA.String("author")
                .AndA.DateTime("date")
                .AndA.String("content")
                .Build();

        final Warehouse warehouse = new Warehouse();
        final Client client = new Client("TweeterAdapter", new URI("ws://localhost:8083/"), warehouse);

        StatusListener listener = new StatusListener(){
            public void onStatus(Status status) {

                GeoLocation loc = status.getGeoLocation();
                if (loc != null) {
                    Thing tweet = BuildANewThing.As(tweetType)
                            .IdentifiedBy("tweet:"+status.getId())
                            .ContainingA.Location(new Location.LatLng(loc.getLatitude(), loc.getLongitude()))
                            .AndA.String("author", status.getUser().getName())
                            .AndA.String("content", status.getText())
                            .AndA.Date("date", status.getCreatedAt())
                            .Build();

                    warehouse.RegisterThing(tweet);
                    client.Send();

                    System.out.println(loc.getLatitude()+":"+loc.getLongitude()+
                            "\t| "+status.getUser().getName() + "\t| " + status.getText());
                }
            }
            public void onDeletionNotice(StatusDeletionNotice statusDeletionNotice) {}
            public void onTrackLimitationNotice(int numberOfLimitedStatuses) {}

            @Override
            public void onScrubGeo(long l, long l2) {}

            @Override
            public void onStallWarning(StallWarning stallWarning) {}

            public void onException(Exception ex) {
                ex.printStackTrace();
            }
        };
        TwitterStream twitterStream = new TwitterStreamFactory().getInstance();
        twitterStream.addListener(listener);
        twitterStream.sample();
    }
}
