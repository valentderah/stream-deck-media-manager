import { action, KeyDownEvent, SingletonAction } from '@elgato/streamdeck';
import { toggleMediaPlayPause } from '../utils/media-manager';

@action({ UUID: 'ru.valentderah.media-manager.media-play-pause' })
export class MediaPlayPauseAction extends SingletonAction {
	override onKeyDown(ev: KeyDownEvent): void {
		toggleMediaPlayPause();
	}
}
