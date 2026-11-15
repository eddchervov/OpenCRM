import audi from '../assets/vehicle-brands/audi.svg'
import bmw from '../assets/vehicle-brands/bmw.svg'
import chevrolet from '../assets/vehicle-brands/chevrolet.svg'
import ford from '../assets/vehicle-brands/ford.svg'
import honda from '../assets/vehicle-brands/honda.svg'
import hyundai from '../assets/vehicle-brands/hyundai.svg'
import kia from '../assets/vehicle-brands/kia.svg'
import mazda from '../assets/vehicle-brands/mazda.svg'
import mercedes from '../assets/vehicle-brands/mercedes.svg'
import mini from '../assets/vehicle-brands/mini.svg'
import mitsubishi from '../assets/vehicle-brands/mitsubishi.svg'
import nissan from '../assets/vehicle-brands/nissan.svg'
import porsche from '../assets/vehicle-brands/porsche.svg'
import renault from '../assets/vehicle-brands/renault.svg'
import skoda from '../assets/vehicle-brands/skoda.svg'
import subaru from '../assets/vehicle-brands/subaru.svg'
import tesla from '../assets/vehicle-brands/tesla.svg'
import toyota from '../assets/vehicle-brands/toyota.svg'
import volkswagen from '../assets/vehicle-brands/volkswagen.svg'
import volvo from '../assets/vehicle-brands/volvo.svg'

const brandLogos = {
  audi: { name: 'Audi', src: audi },
  bmw: { name: 'BMW', src: bmw },
  chevrolet: { name: 'Chevrolet', src: chevrolet },
  ford: { name: 'Ford', src: ford },
  honda: { name: 'Honda', src: honda },
  hyundai: { name: 'Hyundai', src: hyundai },
  kia: { name: 'Kia', src: kia },
  mazda: { name: 'Mazda', src: mazda },
  mercedes: { name: 'Mercedes-Benz', src: mercedes },
  mini: { name: 'MINI', src: mini },
  mitsubishi: { name: 'Mitsubishi', src: mitsubishi },
  nissan: { name: 'Nissan', src: nissan },
  porsche: { name: 'Porsche', src: porsche },
  renault: { name: 'Renault', src: renault },
  skoda: { name: 'Škoda', src: skoda },
  subaru: { name: 'Subaru', src: subaru },
  tesla: { name: 'Tesla', src: tesla },
  toyota: { name: 'Toyota', src: toyota },
  volkswagen: { name: 'Volkswagen', src: volkswagen },
  volvo: { name: 'Volvo', src: volvo }
}

export function getVehicleBrandLogo(logoKey?: string | null) {
  if (!logoKey || !Object.hasOwn(brandLogos, logoKey)) return null

  return brandLogos[logoKey as keyof typeof brandLogos]
}
