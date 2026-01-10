import streamDeck from "@elgato/streamdeck";

import { MediaInfoAction } from "./actions/media-info";
import { MediaNextAction } from "./actions/media-next";
import { MediaPreviousAction } from "./actions/media-previous";
import { MediaPlayPauseAction } from "./actions/media-play-pause";

streamDeck.logger.setLevel("warn");

streamDeck.actions.registerAction(new MediaInfoAction());
streamDeck.actions.registerAction(new MediaNextAction());
streamDeck.actions.registerAction(new MediaPreviousAction());
streamDeck.actions.registerAction(new MediaPlayPauseAction());

streamDeck.connect();
